const { contextBridge, ipcRenderer } = require("electron");

let lastGameplayVisible;
contextBridge.exposeInMainWorld("OverlayDesktop", {
    systemLanguage: process.argv.includes("--overlay-system-language=ru") ? "ru" : "en",
    setGameplayVisible(visible) {
        if (typeof visible !== "boolean" || visible === lastGameplayVisible) return;
        lastGameplayVisible = visible;
        ipcRenderer.send("overlay:gameplay-visible", visible);
    }
});
