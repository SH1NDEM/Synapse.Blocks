# Детская и обычная кампании — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить на сайт выбор между обычной и детской кампаниями с отдельными JSON, прогрессом и решениями.

**Architecture:** `CampaignModeStore` хранит активный режим в localStorage и выдаёт сведения о нём. `LevelStore`, `ProgressStore` и `SolutionStore` получают режим через этот сервис, поэтому загрузка JSON и пользовательские данные всегда относятся к одной кампании. Главная страница выбирает режим, а редактор может выбрать редактируемый JSON.

**Tech Stack:** C# 13, .NET 10, Blazor WebAssembly, System.Text.Json, localStorage через JS interop.

## Global Constraints

- Обычная кампания содержит текущие 10 уровней без изменения их сложности.
- Детская кампания содержит 8 уровней: 01, 02, A, B, C, D, E, F.
- Детские тесты используют только целые неотрицательные числа от 0 до 10; исключены степени, отрицательные числа, дроби, типы и переменные.
- Вступление условия находится перед C; вступление цикла — перед D.
- Обычный прогресс и решения остаются совместимыми с существующими localStorage-ключами.
- Режим для детей использует отдельные ключи localStorage.
- Пользователь явно разрешил выполнение и отправку результата в ветку `master`.

---

### Task 1: Сервис активного режима и раздельное хранение

**Files:**
- Create: `Services/CampaignModeStore.cs`
- Modify: `Program.cs`
- Modify: `Services/LevelStore.cs`
- Modify: `Services/ProgressStore.cs`
- Modify: `Services/SolutionStore.cs`
- Test: `.codex-check/Program.cs`

**Interfaces:**
- Produces `CampaignMode` enum with values `Adult` and `Kids`.
- Produces `CampaignModeStore.GetAsync()`, `CampaignModeStore.SetAsync(CampaignMode)` and `CampaignModeStore.Info(CampaignMode)`.
- `LevelStore.LoadGameAsync()` loads `levels-adult.json` or `levels-kids.json` from the active mode.
- `ProgressStore` and `SolutionStore` derive their localStorage key from the active mode.

- [ ] **Step 1: Write the failing behavior check**

Create a temporary console project under `.codex-check` that references `Models/GameModels.cs`, serialization classes, the new mode service and the three stores. The test must call a pure `CampaignModeStore.Info` contract and assert literal values:

```csharp
Assert.Equal("levels-adult.json", CampaignModeStore.Info(CampaignMode.Adult).LevelsFile);
Assert.Equal("levels-kids.json", CampaignModeStore.Info(CampaignMode.Kids).LevelsFile);
Assert.NotEqual(
    CampaignModeStore.Info(CampaignMode.Adult).ProgressStorageKey,
    CampaignModeStore.Info(CampaignMode.Kids).ProgressStorageKey);
```

- [ ] **Step 2: Run the check and verify it fails**

Run: `dotnet run --project .codex-check/Check.csproj`

Expected: compilation failure because `CampaignModeStore` and `CampaignMode` do not exist.

- [ ] **Step 3: Implement the smallest mode contract**

Create `CampaignModeStore` with a constant `synapse-campaign-mode-v1`, a safe default `CampaignMode.Adult`, and a record containing `LevelsFile`, progress key, solution prefix, display name and short description. Register it as scoped. Change the stores to use `GetAsync()` and the mode information instead of hard-coded JSON names and storage keys. Preserve adult keys exactly: `synapse-csharp-progress-v1` and `synapse-solution-v1-`.

- [ ] **Step 4: Re-run the check and verify it passes**

Run: `dotnet run --project .codex-check/Check.csproj`

Expected: exit code 0 and three contract checks print `PASS`.

- [ ] **Step 5: Commit the storage boundary**

```powershell
git add Program.cs Services/CampaignModeStore.cs Services/LevelStore.cs Services/ProgressStore.cs Services/SolutionStore.cs .codex-check
git commit -m "feat: separate adult and kids campaign storage"
```

### Task 2: JSON кампании для взрослых и детей

**Files:**
- Create: `wwwroot/levels-adult.json`
- Create: `wwwroot/levels-kids.json`
- Test: `.codex-check/Program.cs`

**Interfaces:**
- `levels-adult.json` is deserializable as `List<LevelDefinition>` and contains 10 levels.
- `levels-kids.json` is deserializable as `List<LevelDefinition>` and contains 8 levels.
- Each kids level uses the same serializable `LevelDefinition` model as the existing game.

- [ ] **Step 1: Write failing JSON contract checks**

Extend `.codex-check/Program.cs` to load both files through `JsonSerializer` and assert:

```csharp
Assert.Equal(10, adultLevels.Count);
Assert.Equal(8, kidsLevels.Count);
Assert.Equal("Как работает условие", kidsLevels.Single(level => level.Order == 5).IntroSteps.Single().Title);
Assert.Equal("Как работает цикл", kidsLevels.Single(level => level.Order == 6).IntroSteps.Single().Title);
```

- [ ] **Step 2: Run the check and verify it fails**

Run: `dotnet run --project .codex-check/Check.csproj`

Expected: file-not-found error for `levels-adult.json` and `levels-kids.json`.

- [ ] **Step 3: Create both campaigns**

Copy the current `wwwroot/levels.json` into `levels-adult.json`. Create `levels-kids.json` with child-friendly wording, tests from 0 to 10 only, and the exact A–F sequence from the approved design. Put the existing condition explanation at C (`Order: 5`) with a positive `> 5` example. Put the existing cycle explanation at D (`Order: 6`) with a fixed `3` repeats example. Do not include `Variable` or `VariableAction` in kids allowed blocks.

- [ ] **Step 4: Add executable solutions for all kids tests**

In `.codex-check/Program.cs`, construct one valid `BlockProgram` per kids level and assert every test result equals its `ExpectedOutput`. Use a fixed three-repeat loop for D and F, and an input-driven loop for E.

- [ ] **Step 5: Run the checks and verify they pass**

Run: `dotnet run --project .codex-check/Check.csproj`

Expected: 8 JSON/intro checks and all kids test cases report `PASS`.

- [ ] **Step 6: Commit the campaign data**

```powershell
git add wwwroot/levels-adult.json wwwroot/levels-kids.json .codex-check
git commit -m "feat: add child-friendly campaign levels"
```

### Task 3: Выбор версии в интерфейсе игрока

**Files:**
- Modify: `Pages/Landing.razor`
- Modify: `Layout/MainLayout.razor`
- Modify: `Pages/Home.razor`
- Modify: `wwwroot/css/app.css`
- Test: `dotnet build Synapse.Blocks.slnx`

**Interfaces:**
- `Landing.SelectModeAsync(CampaignMode)` saves the mode and navigates to `game`.
- Landing displays labels and descriptions from `CampaignModeStore.Info`.
- `Home` renders order labels from the selected campaign: 01/02 for first two kids levels and A–F for the rest.
- Reset continues to clear only active profile storage.

- [ ] **Step 1: Add the two card controls**

Inject `CampaignModeStore` into `Landing.razor`; load the active mode before levels. Add two accessible buttons labelled «Для детей 1–3 класс» and «Обычная версия». Their click calls `SetAsync(mode)`, then navigates to `game`.

- [ ] **Step 2: Add selected-state and responsive styling**

Add scoped-looking CSS selectors under the existing landing styles. The active card has a visible border and status; the kids card uses a calm blue/cyan accent and does not replace the existing site palette.

- [ ] **Step 3: Render child labels in game views**

Add a small view helper in `Home.razor` and `Landing.razor`: if active mode is kids and `Order >= 3`, label the level with `A`, `Б`, `C`, `D`, `E`, `F`; otherwise format the numeric order as two digits. Use it in the level selector, city labels, and mission cards.

- [ ] **Step 4: Build the web project**

Run: `dotnet build Synapse.Blocks.slnx --no-restore`

Expected: build exits 0 with no errors.

- [ ] **Step 5: Commit the player chooser**

```powershell
git add Pages/Landing.razor Pages/Home.razor Layout/MainLayout.razor wwwroot/css/app.css
git commit -m "feat: let players choose campaign version"
```

### Task 4: Редактор уровней для двух JSON

**Files:**
- Modify: `Pages/LevelEditor.razor`
- Modify: `Services/LevelStore.cs`
- Test: `dotnet build Synapse.Blocks.slnx`

**Interfaces:**
- `LevelStore.LoadEditorAsync(CampaignMode mode)` loads a selected file without changing the player’s active mode.
- `LevelEditor` lets an admin switch between «Обычная» and «Для детей», reloads that set and exports `levels-adult.json` or `levels-kids.json`.

- [ ] **Step 1: Add the editor mode selector**

Add a two-button selector in the editor sidebar. The selected mode is local editor state, defaults to adult, and switching discards only unsaved editor changes after a confirmation dialogue.

- [ ] **Step 2: Bind loading, reset and export to the selected mode**

Call `LoadEditorAsync(_editorMode)` for initial load, mode switch and reload. Use `CampaignModeStore.Info(_editorMode).LevelsFile` as the download name and in the save confirmation message.

- [ ] **Step 3: Build the web project**

Run: `dotnet build Synapse.Blocks.slnx --no-restore`

Expected: build exits 0 with no errors.

- [ ] **Step 4: Commit the editor selector**

```powershell
git add Pages/LevelEditor.razor Services/LevelStore.cs
git commit -m "feat: edit adult and kids level sets"
```

### Task 5: Финальная проверка и публикация

**Files:**
- Delete: `.codex-check/Program.cs`
- Delete: `.codex-check/Check.csproj`

- [ ] **Step 1: Remove only temporary check source files**

Use an explicit patch to remove `.codex-check/Program.cs` and `.codex-check/Check.csproj`. Keep the existing ignored `bin` and `obj` directories untouched.

- [ ] **Step 2: Verify JSON and application**

Run:

```powershell
dotnet build Synapse.Blocks.slnx --no-restore
git diff --check
```

Expected: build exit code 0 and no diff whitespace errors.

- [ ] **Step 3: Verify the implementation checklist**

Confirm from the built files that adult JSON has 10 levels, kids JSON has 8 levels, C/D contain the two introductory explanations, no kids test contains a negative number or power, modes have distinct localStorage keys, and the selector is shown on the landing page and in the editor.

- [ ] **Step 4: Commit and push to the requested branch**

```powershell
git add -A
git commit -m "feat: add adult and kids campaign modes"
git push origin master:main
```
