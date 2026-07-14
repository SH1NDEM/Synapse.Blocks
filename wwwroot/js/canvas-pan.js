// Камера перемещается в браузере без задержек Blazor, но только в границах блоков.
// Сам мир растёт динамически: ограничения берутся из data-атрибутов node-canvas.
(() => {
    const interactiveSelector = ".node-card, .wire-hit, button, input, select, label, a, [draggable='true']";
    const observers = new WeakMap();
    let drag = null;

    const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
    const readNumber = (canvas, name) => Number.parseFloat(canvas.dataset[name] || "0") || 0;

    function limits(canvas) {
        if (canvas.dataset.hasContent !== "true") return null;

        const zoom = Math.max(.01, readNumber(canvas, "zoom") || 1);
        const minX = readNumber(canvas, "contentMinX") * zoom;
        const minY = readNumber(canvas, "contentMinY") * zoom;
        const maxX = readNumber(canvas, "contentMaxX") * zoom;
        const maxY = readNumber(canvas, "contentMaxY") * zoom;
        const nativeMaxLeft = Math.max(0, canvas.scrollWidth - canvas.clientWidth);
        const nativeMaxTop = Math.max(0, canvas.scrollHeight - canvas.clientHeight);

        // Буфер почти равен целому экрану, но 32 px крайнего блока всегда остаются
        // видимыми. Формула одинакова для одного блока и для большой схемы.
        const visibleX = Math.min(32, canvas.clientWidth / 4);
        const visibleY = Math.min(32, canvas.clientHeight / 4);
        const minLeft = clamp(minX - (canvas.clientWidth - visibleX), 0, nativeMaxLeft);
        const maxLeft = clamp(maxX - visibleX, minLeft, nativeMaxLeft);
        const minTop = clamp(minY - (canvas.clientHeight - visibleY), 0, nativeMaxTop);
        const maxTop = clamp(maxY - visibleY, minTop, nativeMaxTop);

        return { minLeft, maxLeft, minTop, maxTop };
    }

    function constrain(canvas, desiredLeft = canvas.scrollLeft, desiredTop = canvas.scrollTop) {
        const bounds = limits(canvas);
        if (!bounds) return;

        canvas.scrollLeft = clamp(desiredLeft, bounds.minLeft, bounds.maxLeft);
        canvas.scrollTop = clamp(desiredTop, bounds.minTop, bounds.maxTop);
    }

    function observeBounds(canvas) {
        if (observers.has(canvas)) return;

        let frame = 0;
        let originX = readNumber(canvas, "originX");
        let originY = readNumber(canvas, "originY");
        const schedule = () => {
            const nextOriginX = readNumber(canvas, "originX");
            const nextOriginY = readNumber(canvas, "originY");
            if (canvas.dataset.hasContent === "true") {
                const zoom = Math.max(.01, readNumber(canvas, "zoom") || 1);
                // Компенсируем сдвиг внутреннего начала координат: неподвижные блоки
                // остаются на экране на своих местах, пока крайний блок уходит в минус.
                canvas.scrollLeft += (nextOriginX - originX) * zoom;
                canvas.scrollTop += (nextOriginY - originY) * zoom;
            }
            originX = nextOriginX;
            originY = nextOriginY;
            cancelAnimationFrame(frame);
            frame = requestAnimationFrame(() => {
                if (!canvas.classList.contains("is-node-dragging")) constrain(canvas);
            });
        };
        const mutation = new MutationObserver(schedule);
        mutation.observe(canvas, { attributes: true });
        const resize = new ResizeObserver(schedule);
        resize.observe(canvas);
        observers.set(canvas, { mutation, resize });
    }

    document.addEventListener("pointerdown", event => {
        const canvas = event.target instanceof Element ? event.target.closest(".node-canvas") : null;
        if (!canvas || event.button !== 0 || event.target.closest(interactiveSelector)) return;

        const bounds = limits(canvas);
        if (!bounds) return; // Поле без блоков перемещать нельзя.
        const canMoveX = bounds.maxLeft - bounds.minLeft > .5;
        const canMoveY = bounds.maxTop - bounds.minTop > .5;
        if (!canMoveX && !canMoveY) return;

        const canvasRect = canvas.getBoundingClientRect();
        if (event.clientX > canvasRect.left + canvas.clientWidth || event.clientY > canvasRect.top + canvas.clientHeight) return;

        drag = {
            canvas,
            pointerId: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            startScrollLeft: canvas.scrollLeft,
            startScrollTop: canvas.scrollTop
        };

        canvas.classList.add("is-panning");
        canvas.setPointerCapture?.(event.pointerId);
        event.preventDefault();
    });

    document.addEventListener("pointermove", event => {
        if (!drag || drag.pointerId !== event.pointerId) return;
        const bounds = limits(drag.canvas);
        if (!bounds) return;

        const desiredLeft = drag.startScrollLeft - (event.clientX - drag.startX);
        const desiredTop = drag.startScrollTop - (event.clientY - drag.startY);
        drag.canvas.scrollLeft = clamp(desiredLeft, bounds.minLeft, bounds.maxLeft);
        drag.canvas.scrollTop = clamp(desiredTop, bounds.minTop, bounds.maxTop);
        event.preventDefault();
    });

    const finish = event => {
        if (!drag || drag.pointerId !== event.pointerId) return;
        drag.canvas.classList.remove("is-panning");
        if (drag.canvas.hasPointerCapture?.(event.pointerId)) drag.canvas.releasePointerCapture(event.pointerId);
        constrain(drag.canvas);
        drag = null;
    };

    document.addEventListener("pointerup", finish);
    document.addEventListener("pointercancel", finish);

    window.synapseCanvas = {
        center(canvas, left, top) {
            observeBounds(canvas);
            if (canvas.dataset.hasContent === "true") {
                constrain(canvas, left, top);
            } else {
                // Пустая схема не двигается мышью, но сохраняет внутренний запас,
                // чтобы первый поставленный блок сразу мог стать подвижным якорем.
                canvas.scrollLeft = Math.max(0, left);
                canvas.scrollTop = Math.max(0, top);
            }
        },
        refresh(canvas, emptyLeft, emptyTop) {
            observeBounds(canvas);
            if (canvas.dataset.hasContent === "true") {
                // Сохраняем текущий ракурс, только подрезая его новыми границами и буфером.
                constrain(canvas);
            } else {
                canvas.scrollLeft = Math.max(0, emptyLeft);
                canvas.scrollTop = Math.max(0, emptyTop);
            }
        },
        constrain,
        worldPoint(canvas, clientX, clientY, zoom) {
            const bounds = canvas.getBoundingClientRect();
            const scale = zoom || 1;
            return [
                (clientX - bounds.left + canvas.scrollLeft) / scale,
                (clientY - bounds.top + canvas.scrollTop) / scale
            ];
        }
    };
})();
