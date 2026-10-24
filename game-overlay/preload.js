const { contextBridge, ipcRenderer } = require("electron");

let lastGameplayVisible;
contextBridge.exposeInMainWorld("OverlayDesktop", {
    setGameplayVisible(visible) {
        if (typeof visible !== "boolean" || visible === lastGameplayVisible) return;
        lastGameplayVisible = visible;
        ipcRenderer.send("overlay:gameplay-visible", visible);
    }
});
