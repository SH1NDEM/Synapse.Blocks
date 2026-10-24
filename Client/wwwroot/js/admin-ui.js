window.synapseDialogs = {
    open(dialog) {
        dialog.addEventListener('cancel', event => event.preventDefault());
        if (!dialog.open) dialog.showModal();
    },
    close(dialog) {
        if (dialog.open) dialog.close();
    }
};
window.synapseAdmin = {
    openDialog(dialog) { window.synapseDialogs.open(dialog); },
    keepDialogOpen(dialog) {
        if (!dialog.open) dialog.showModal();
    }
};
