const OverlayApp = (() => {
    const storageKey = "overlay-control-state-v2";
    const channelName = "overlay-control-channel";
    const channel = "BroadcastChannel" in window ? new BroadcastChannel(channelName) : null;
    const serverSyncEnabled = window.location.protocol === "http:" || window.location.protocol === "https:";

    const monsterConfig = {
        levels: {
            1: { monsters: ["Peeper", "Shadow Child", "Gnomes", "Apex Predator", "Spewer", "Bella", "Birthday Boy", "Elsa", "Tick"] },
            2: { monsters: ["Rugrat", "Animal", "Upscream", "Chef", "Hidden", "Bowtie", "Mentalist", "Banger", "Gambit", "Headgrab", "Heart Hugger", "Oogly"] },
            3: { monsters: ["Headman", "Robe", "Huntsman", "Reaper", "Clown", "Trudge", "Cleanup Crew", "Loom"] }
        },
        strength: {
            Banger: 0,
            Gnome: 0,
            Gnomes: 0,
            Tick: 0,
            Animal: 4,
            "Birthday Boy": 4,
            Headgrab: 4,
            Mentalist: 4,
            Hidden: 4,
            Rugrat: 4,
            Spewer: 4,
            Upscream: 4,
            Bowtie: 7,
            Bella: 9,
            Chef: 9,
            Gambit: 9,
            "Heart Hugger": 9,
            Huntsman: 9,
            Oogly: 9,
            Reaper: 9,
            "Shadow Child": 9,
            "Cleanup Crew": 13,
            Clown: 13,
            Headman: 13,
            Loom: 13,
            Robe: 13,
            Trudge: 13,
            "Apex Predator": "impossible",
            Elsa: "impossible",
            Peeper: "impossible"
        },
        replacements: [
            "Animals", "Upscreams", "Bowties", "Rugrat", "Mentalists", "Peepers",
            "Hidden", "Apex Predators", "Chefs", "Spewers", "Shadow Children",
            "Bangers", "Gnomes", "Bella", "Birthday Boy", "Elsa", "Gambit",
            "Headgrab", "Heart Hugger", "Oogly", "Tick"
        ]
    };

    const levelMonsterCounts = {
        "1-2": { level1: 1, level2: 0, level3: 1 },
        "3-5": { level1: 1, level2: 1, level3: 1 },
        "6-8": { level1: 2, level2: 2, level3: 2 },
        "9": { level1: 2, level2: 3, level3: 2 },
        "10-19": { level1: 2, level2: 3, level3: 3 },
        "20+": { level1: 3, level2: 4, level3: 4 }
    };

    const defaultState = {
        level: 1,
        strength: 0,
        style: 1,
        bgEnabled: true,
        timerVisible: false,
        squareSize: 50,
        columnsCount: 4,
        seconds: 0,
        running: false,
        startedAt: null,
        monsters: []
    };

    let state = defaultState;
    state = normalizeState(loadState());
    const listeners = new Set();
    let serverSyncReady = false;

    function loadState() {
        try {
            const raw = localStorage.getItem(storageKey);
            return raw ? JSON.parse(raw) : defaultState;
        } catch {
            return state || defaultState;
        }
    }

    function normalizeState(nextState) {
        const source = nextState || defaultState;
        return {
            ...defaultState,
            ...source,
            monsters: Array.isArray(source.monsters) ? source.monsters : []
        };
    }

    function getState() {
        state = normalizeState(loadState());
        return { ...state, monsters: [...state.monsters] };
    }

    function saveState(nextState, shouldBroadcast = true) {
        state = normalizeState(nextState);
        try {
            localStorage.setItem(storageKey, JSON.stringify(state));
        } catch {
            // Some file:// or OBS contexts can block storage; live sync can still use BroadcastChannel.
        }
        listeners.forEach((listener) => listener(getState()));
        if (shouldBroadcast && channel) {
            channel.postMessage(state);
        }
        if (shouldBroadcast && serverSyncEnabled && serverSyncReady) {
            postStateToServer(state);
        }
    }

    async function postStateToServer(nextState) {
        try {
            await fetch("/api/state", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ state: nextState })
            });
        } catch {
            serverSyncReady = false;
        }
    }

    async function initializeServerSync() {
        if (!serverSyncEnabled) return;

        try {
            const response = await fetch("/api/state", { cache: "no-store" });
            if (!response.ok) return;

            serverSyncReady = true;
            const payload = await response.json();
            if (payload.state) {
                saveState(payload.state, false);
            } else {
                postStateToServer(getState());
            }
        } catch {
            serverSyncReady = false;
            return;
        }

        if ("EventSource" in window) {
            const events = new EventSource("/api/events");
            events.onmessage = (event) => {
                if (!event.data) return;
                try {
                    saveState(JSON.parse(event.data), false);
                } catch {
                    // Ignore malformed server events.
                }
            };
            events.onerror = () => {
                serverSyncReady = false;
            };
            events.onopen = () => {
                serverSyncReady = true;
            };
        }
    }

    function updateState(updater) {
        const nextState = typeof updater === "function" ? updater(getState()) : updater;
        saveState(nextState);
    }

    function subscribe(listener) {
        listeners.add(listener);
        listener(getState());
        return () => listeners.delete(listener);
    }

    if (channel) {
        channel.addEventListener("message", (event) => {
            saveState(event.data, false);
        });
    }

    window.addEventListener("storage", (event) => {
        if (event.key === storageKey) {
            listeners.forEach((listener) => listener(getState()));
        }
    });

    initializeServerSync();

    function getCountsForLevel(level) {
        if (level <= 2) return levelMonsterCounts["1-2"];
        if (level <= 5) return levelMonsterCounts["3-5"];
        if (level <= 8) return levelMonsterCounts["6-8"];
        if (level === 9) return levelMonsterCounts["9"];
        if (level <= 19) return levelMonsterCounts["10-19"];
        return levelMonsterCounts["20+"];
    }

    function getRecommendedColumns(level) {
        const counts = getCountsForLevel(level);
        const totalMonsters = counts.level1 + counts.level2 + counts.level3;
        if (level >= 20) return 7;
        if (level >= 6) return Math.min(totalMonsters, 8);
        return 4;
    }

    function getTimerSeconds(currentState) {
        if (!currentState.running || !currentState.startedAt) {
            return currentState.seconds;
        }
        return currentState.seconds + Math.floor((Date.now() - currentState.startedAt) / 1000);
    }

    function formatTime(totalSeconds) {
        const mins = Math.floor(totalSeconds / 60);
        const secs = totalSeconds % 60;
        return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`;
    }

    function getRespawnHtml(seconds) {
        const minutes = Math.floor(seconds / 60);
        let respawnText;

        if (minutes < 10) respawnText = "4-5 МИНУТ";
        else if (minutes < 20) respawnText = "3.2-4 МИНУТЫ";
        else if (minutes < 30) respawnText = "2.4-3 МИНУТЫ";
        else if (minutes < 40) respawnText = "1.6-2 МИНУТЫ";
        else if (minutes < 50) respawnText = "0.8-1 МИНУТА";
        else respawnText = "1 СЕКУНДА";

        const noDespawnText = seconds <= 240 ? ' <span class="no-despawn">(БЕЗ ДЕСПАВНА)</span>' : "";
        return `🔄 ${respawnText}${noDespawnText}`;
    }

    function getMonsterFileName(monsterName) {
        const replacements = {
            peeper: "eye_monster",
            peepers: "eye_monster",
            "shadow child": "shadow_child",
            "shadow children": "shadow_child",
            gnomes: "gnome",
            "apex predator": "apex_predator",
            "apex predators": "apex_predator",
            chef: "chef_frog",
            chefs: "chef_frog",
            clown: "clown_beamer",
            animals: "animal",
            upscreams: "upscream",
            bowties: "bowtie",
            mentalists: "mentalist",
            spewers: "spewer",
            bangers: "banger"
        };
        const key = monsterName.toLowerCase();
        return replacements[key] || key;
    }

    function getMonsterImage(monsterName) {
        return `monsters/${getMonsterFileName(monsterName)}.webp`;
    }

    function getMonsterCount(monsterName, level, isReplacement = false) {
        if (monsterName === "Gnomes" && level !== 3) return 4;
        if ((monsterName === "Bangers" || monsterName === "Banger") && level === 2) return 3;
        if (level !== 3 || !isReplacement) return null;
        if (["Animals", "Upscreams", "Bowties", "Rugrat", "Mentalists", "Peepers", "Chefs"].includes(monsterName)) return 3;
        if (monsterName === "Hidden") return 2;
        if (["Apex Predators", "Spewers", "Shadow Children"].includes(monsterName)) return 4;
        if (monsterName === "Bangers") return 6;
        if (monsterName === "Gnomes") return 10;
        if (["Bella", "Birthday Boy", "Elsa", "Headgrab", "Tick"].includes(monsterName)) return 3;
        if (["Gambit", "Heart Hugger", "Oogly"].includes(monsterName)) return 2;
        return null;
    }

    function getMonsterStrength(monsterName) {
        if (monsterConfig.strength[monsterName] !== undefined) return monsterConfig.strength[monsterName];
        if (monsterName.endsWith("s")) {
            return monsterConfig.strength[monsterName.slice(0, -1)];
        }
        return undefined;
    }

    function createMonsterEntry(monsterName, level, isReplacement = false) {
        return {
            id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
            level,
            name: monsterName,
            image: getMonsterImage(monsterName),
            count: getMonsterCount(monsterName, level, isReplacement),
            strength: getMonsterStrength(monsterName)
        };
    }

    function trimMonstersForLevel(currentState) {
        const counts = getCountsForLevel(currentState.level);
        const kept = [];
        for (let level = 1; level <= 3; level++) {
            const max = counts[`level${level}`];
            kept.push(...currentState.monsters.filter((monster) => monster.level === level).slice(0, max));
        }
        return kept;
    }

    function setLevel(level) {
        updateState((currentState) => {
            const nextState = {
                ...currentState,
                level,
                seconds: 0,
                running: false,
                startedAt: null,
                columnsCount: getRecommendedColumns(level)
            };
            nextState.monsters = trimMonstersForLevel(nextState);
            return nextState;
        });
    }

    function setStrength(strength) {
        updateState((currentState) => ({ ...currentState, strength }));
    }

    function startTimer() {
        updateState((currentState) => {
            if (currentState.running) return currentState;
            return { ...currentState, running: true, startedAt: Date.now() };
        });
    }

    function stopTimer() {
        updateState((currentState) => ({
            ...currentState,
            seconds: getTimerSeconds(currentState),
            running: false,
            startedAt: null
        }));
    }

    function resetTimer() {
        updateState((currentState) => ({ ...currentState, seconds: 0, running: false, startedAt: null }));
    }

    function addMonster(monsterName, level, isReplacement = false) {
        updateState((currentState) => {
            const counts = getCountsForLevel(currentState.level);
            const currentLevelCount = currentState.monsters.filter((monster) => monster.level === level).length;
            if (currentLevelCount >= counts[`level${level}`]) return currentState;
            return {
                ...currentState,
                monsters: [...currentState.monsters, createMonsterEntry(monsterName, level, isReplacement)]
            };
        });
    }

    function removeMonster(monsterName, level) {
        updateState((currentState) => {
            const index = currentState.monsters.findIndex((monster) => monster.level === level && monster.name === monsterName);
            if (index === -1) return currentState;
            return {
                ...currentState,
                monsters: currentState.monsters.filter((_, monsterIndex) => monsterIndex !== index)
            };
        });
    }

    function clearMonsters() {
        updateState((currentState) => ({ ...currentState, monsters: [] }));
    }

    function setStyle(style) {
        updateState((currentState) => ({ ...currentState, style }));
    }

    function setBgEnabled(bgEnabled) {
        updateState((currentState) => ({ ...currentState, bgEnabled }));
    }

    function setTimerVisible(timerVisible) {
        updateState((currentState) => ({ ...currentState, timerVisible }));
    }

    function setSquareSize(squareSize) {
        updateState((currentState) => ({ ...currentState, squareSize }));
    }

    function setColumnsCount(columnsCount) {
        updateState((currentState) => ({ ...currentState, columnsCount }));
    }

    return {
        monsterConfig,
        addMonster,
        clearMonsters,
        formatTime,
        getCountsForLevel,
        getMonsterCount,
        getMonsterImage,
        getRespawnHtml,
        getState,
        getTimerSeconds,
        removeMonster,
        resetTimer,
        setBgEnabled,
        setColumnsCount,
        setLevel,
        setSquareSize,
        setStrength,
        setStyle,
        setTimerVisible,
        startTimer,
        stopTimer,
        subscribe
    };
})();
