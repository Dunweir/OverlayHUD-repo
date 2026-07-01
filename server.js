const http = require("http");
const fs = require("fs");
const path = require("path");

const host = process.env.HOST || "0.0.0.0";
const port = Number(process.env.PORT || 8787);
let serverRoot = __dirname;
const clients = new Set();

let overlayState = null;

const monsterConfig = {
    levels: {
        1: ["Peeper", "Shadow Child", "Gnomes", "Apex Predator", "Spewer", "Bella", "Birthday Boy", "Elsa", "Tick"],
        2: ["Rugrat", "Animal", "Upscream", "Chef", "Hidden", "Bowtie", "Mentalist", "Banger", "Gambit", "Headgrab", "Heart Hugger", "Oogly"],
        3: ["Headman", "Robe", "Huntsman", "Reaper", "Clown", "Trudge", "Cleanup Crew", "Loom"]
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
    aliases: {
        apexpredator: "Apex Predator",
        animal: "Animal",
        animals: "Animals",
        banger: "Banger",
        bangers: "Bangers",
        bella: "Bella",
        birthdayboy: "Birthday Boy",
        bowtie: "Bowtie",
        bowties: "Bowties",
        chef: "Chef",
        cheffrog: "Chef",
        chefs: "Chefs",
        cleanupcrew: "Cleanup Crew",
        clown: "Clown",
        clownbeamer: "Clown",
        elsa: "Elsa",
        gambit: "Gambit",
        gnome: "Gnomes",
        gnomes: "Gnomes",
        headgrab: "Headgrab",
        headman: "Headman",
        hearthugger: "Heart Hugger",
        hidden: "Hidden",
        huntsman: "Huntsman",
        loom: "Loom",
        mentalist: "Mentalist",
        mentalists: "Mentalists",
        oogly: "Oogly",
        peeper: "Peeper",
        peepers: "Peepers",
        reaper: "Reaper",
        robe: "Robe",
        rugrat: "Rugrat",
        shadowchild: "Shadow Child",
        spewer: "Spewer",
        spewers: "Spewers",
        tick: "Tick",
        trudge: "Trudge",
        upscream: "Upscream",
        upscreams: "Upscreams"
    }
};

const levelMonsterCounts = {
    "1-2": { level1: 1, level2: 0, level3: 1 },
    "3-5": { level1: 1, level2: 1, level3: 1 },
    "6-8": { level1: 2, level2: 2, level3: 2 },
    "9": { level1: 2, level2: 3, level3: 2 },
    "10-19": { level1: 2, level2: 3, level3: 3 },
    "20+": { level1: 3, level2: 4, level3: 4 }
};

const defaultPlayerUpgrades = {
    strength: 0,
    tumbleLaunch: 0,
    range: 0,
    sprintSpeed: 0,
    mapPlayerCount: 0,
    tumbleWings: 0,
    crouchRest: 0,
    extraJump: 0,
    tumbleClimb: 0
};

const defaultOverlayState = {
    level: 1,
    gameplayVisible: false,
    tabHidden: false,
    ...defaultPlayerUpgrades,
    style: 1,
    interfaceLanguage: "ru",
    bgEnabled: false,
    timerVisible: false,
    upgradesVisible: true,
    monsterIconsVisible: true,
    levelBadgeVisible: true,
    upgradeTooltipsVisible: true,
    monsterHealthBarsVisible: true,
    monsterStrengthVisible: true,
    respawnTimerVisible: true,
    respawnIndicatorVisible: true,
    squareSize: 70,
    upgradeSize: 38,
    overlayScaleVersion: 3,
    columnsCount: 7,
    columnsLayoutVersion: 1,
    hoverOpacity: 50,
    seconds: 0,
    running: false,
    startedAt: null,
    monsters: [],
    roster: [],
    rosterPending: false
};

function normalizeOverlayState(rawState) {
    if (!rawState) return null;

    const source = { ...defaultOverlayState, ...rawState };
    const sourceSquareSize = Number(source.squareSize);
    const sourceUpgradeSize = Number(source.upgradeSize);
    const shouldMigrateDefaultSizes = Number(source.overlayScaleVersion) < 3;
    const squareSize = Number.isFinite(sourceSquareSize)
        ? (shouldMigrateDefaultSizes && (sourceSquareSize === 50 || sourceSquareSize === 64) ? defaultOverlayState.squareSize : sourceSquareSize)
        : defaultOverlayState.squareSize;
    const upgradeSize = Number.isFinite(sourceUpgradeSize)
        ? (shouldMigrateDefaultSizes && sourceUpgradeSize === 32 ? defaultOverlayState.upgradeSize : sourceUpgradeSize)
        : defaultOverlayState.upgradeSize;
    const columnsCount = source.columnsLayoutVersion === 1 ? source.columnsCount : defaultOverlayState.columnsCount;
    const hoverOpacity = Number.isFinite(Number(source.hoverOpacity))
        ? Math.min(100, Math.max(20, Number(source.hoverOpacity)))
        : defaultOverlayState.hoverOpacity;
    const interfaceLanguage = source.interfaceLanguage === "en" ? "en" : defaultOverlayState.interfaceLanguage;

    return {
        ...source,
        style: 1,
        squareSize,
        upgradeSize,
        overlayScaleVersion: defaultOverlayState.overlayScaleVersion,
        columnsCount,
        columnsLayoutVersion: defaultOverlayState.columnsLayoutVersion,
        hoverOpacity,
        interfaceLanguage,
        monsters: Array.isArray(source.monsters) ? source.monsters : [],
        roster: Array.isArray(source.roster) ? source.roster : []
    };
}

const mimeTypes = {
    ".html": "text/html; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".ttf": "font/ttf",
    ".otf": "font/otf",
    ".webp": "image/webp"
};

function sendJson(response, statusCode, payload) {
    response.writeHead(statusCode, {
        "Content-Type": "application/json; charset=utf-8",
        "Cache-Control": "no-store"
    });
    response.end(JSON.stringify(payload));
}

function readBody(request) {
    return new Promise((resolve, reject) => {
        let body = "";
        request.on("data", (chunk) => {
            body += chunk;
            if (body.length > 1024 * 1024) {
                request.destroy();
                reject(new Error("Request body is too large"));
            }
        });
        request.on("end", () => resolve(body));
        request.on("error", reject);
    });
}

function broadcastState() {
    const data = `data: ${JSON.stringify(overlayState)}\n\n`;
    for (const client of clients) {
        client.write(data);
    }
}

function normalizeMonsterKey(name) {
    return String(name || "").toLowerCase().replace(/[^a-z0-9]/g, "");
}

function normalizeMonsterName(name) {
    const key = normalizeMonsterKey(name);
    return monsterConfig.aliases[key] || null;
}

function getCountsForLevel(level) {
    if (level <= 2) return levelMonsterCounts["1-2"];
    if (level <= 5) return levelMonsterCounts["3-5"];
    if (level <= 8) return levelMonsterCounts["6-8"];
    if (level === 9) return levelMonsterCounts["9"];
    if (level <= 19) return levelMonsterCounts["10-19"];
    return levelMonsterCounts["20+"];
}

function normalizeLevel(value) {
    const level = Number(value);
    if (!Number.isFinite(level) || level < 1) return null;
    return Math.floor(level);
}

function getMonsterLevel(monsterName) {
    for (let level = 1; level <= 3; level++) {
        if (monsterConfig.levels[level].includes(monsterName)) return level;
    }

    return 3;
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

function createMonsterEntry(monsterName, level, isReplacement, sourceId = null) {
    return {
        id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
        level,
        name: monsterName,
        image: `monsters/${getMonsterFileName(monsterName)}.webp`,
        count: getMonsterCount(monsterName, level, isReplacement),
        strength: getMonsterStrength(monsterName),
        sourceId
    };
}

function addSeenMonster(rawName, rawSourceId) {
    const monsterName = normalizeMonsterName(rawName);
    if (!monsterName) {
        return { ok: false, statusCode: 422, payload: { error: "Unknown monster", received: rawName } };
    }

    const state = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        monsters: Array.isArray(overlayState?.monsters) ? overlayState.monsters : [],
        roster: Array.isArray(overlayState?.roster) ? overlayState.roster : []
    };
    const level = getMonsterLevel(monsterName);
    const isReplacement = level === 3 && !monsterConfig.levels[3].includes(monsterName);
    const monsterCount = getMonsterCount(monsterName, level, isReplacement);
    const rawId = rawSourceId == null ? null : String(rawSourceId);
    const rosterSlot = rawId == null ? null : state.roster.find((slot) => Array.isArray(slot.sourceIds) && slot.sourceIds.includes(rawId));
    const sourceId = rosterSlot?.id || (rawId == null ? null : `enemy:${rawId}`);

    if ((sourceId != null && state.monsters.some((monster) => monster.sourceId === sourceId))
        || (state.roster.length === 0 && monsterCount != null && state.monsters.some((monster) => normalizeMonsterKey(monster.name) === normalizeMonsterKey(monsterName)))) {
        overlayState = state;
        return { ok: true, statusCode: 200, payload: { ok: true, added: false, monster: monsterName, reason: "already-known" } };
    }

    if (state.roster.length === 0) {
        const counts = getCountsForLevel(state.level);
        const currentLevelCount = state.monsters.filter((monster) => monster.level === level).length;
        if (currentLevelCount >= counts[`level${level}`]) {
            overlayState = state;
            return { ok: true, statusCode: 200, payload: { ok: true, added: false, monster: monsterName, reason: "level-slot-limit" } };
        }
    }

    overlayState = {
        ...state,
        monsters: [...state.monsters, createMonsterEntry(monsterName, level, isReplacement, sourceId)]
    };
    return { ok: true, statusCode: 200, payload: { ok: true, added: true, monster: monsterName, level } };
}

function updateMonsterRoster(rawMonsters) {
    if (!Array.isArray(rawMonsters)) {
        return { ok: false, statusCode: 422, payload: { error: "Invalid monster roster" } };
    }

    const state = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        monsters: Array.isArray(overlayState?.monsters) ? overlayState.monsters : [],
        roster: Array.isArray(overlayState?.roster) ? overlayState.roster : []
    };
    const roster = state.roster.map((slot) => ({ ...slot, sourceIds: Array.isArray(slot.sourceIds) ? [...slot.sourceIds] : [] }));

    for (const rawMonster of rawMonsters) {
        const monsterName = normalizeMonsterName(rawMonster?.name);
        if (!monsterName || rawMonster?.id == null) continue;

        const sourceId = String(rawMonster.id);
        const level = getMonsterLevel(monsterName);
        const isReplacement = level === 3 && !monsterConfig.levels[3].includes(monsterName);
        const groupSize = getMonsterCount(monsterName, level, isReplacement);
        const groupKey = groupSize == null ? null : normalizeMonsterKey(monsterName);
        let slot = roster.find((entry) => Array.isArray(entry.sourceIds) && entry.sourceIds.includes(sourceId));
        if (!slot && groupKey != null) {
            const groupSlots = roster.filter((entry) => entry.groupKey === groupKey);
            slot = groupSlots.find((entry) => entry.sourceIds.length < groupSize);
            if (!slot) {
                slot = { id: `group:${groupKey}:${groupSlots.length + 1}`, level, groupKey, sourceIds: [] };
                roster.push(slot);
            }
        }
        const slotId = `enemy:${sourceId}`;
        if (!slot && groupKey == null) slot = roster.find((entry) => entry.id === slotId);
        if (!slot) {
            slot = { id: slotId, level, sourceIds: [] };
            roster.push(slot);
        }
        if (!slot.sourceIds.includes(sourceId)) slot.sourceIds.push(sourceId);
    }

    roster.sort((left, right) => left.level - right.level || left.id.localeCompare(right.id));
    overlayState = { ...state, roster, rosterPending: false };
    return { ok: true, statusCode: 200, payload: { ok: true, slots: roster.length } };
}

function updateMonsterStatuses(rawStatuses) {
    if (!Array.isArray(rawStatuses)) {
        return { ok: false, statusCode: 422, payload: { error: "Invalid monster statuses" } };
    }

    const state = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        monsters: Array.isArray(overlayState?.monsters) ? overlayState.monsters : [],
        roster: Array.isArray(overlayState?.roster) ? overlayState.roster : []
    };
    const statuses = new Map();
    for (const rawStatus of rawStatuses) {
        if (rawStatus?.id == null || typeof rawStatus.alive !== "boolean") continue;
        const remaining = Number(rawStatus.respawnRemaining);
        const health = Number(rawStatus.health);
        const maxHealth = Number(rawStatus.maxHealth);
        statuses.set(String(rawStatus.id), {
            alive: rawStatus.alive,
            respawnRemaining: Number.isFinite(remaining) ? Math.max(0, remaining) : 0,
            health: Number.isFinite(health) ? Math.max(0, health) : null,
            maxHealth: Number.isFinite(maxHealth) ? Math.max(0, maxHealth) : null
        });
    }

    const now = Date.now();
    let updatedSlots = 0;
    const roster = state.roster.map((slot) => {
        const sourceIds = Array.isArray(slot.sourceIds) ? slot.sourceIds.map(String) : [];
        const sourceStatuses = sourceIds.map((id) => statuses.get(id)).filter(Boolean);
        if (sourceStatuses.length === 0 || sourceStatuses.length !== sourceIds.length) return slot;

        updatedSlots++;
        const healthStatuses = sourceStatuses.filter((status) => status.health != null);
        const healthPatch = healthStatuses.length > 0
            ? {
                health: healthStatuses.reduce((total, status) => total + status.health, 0),
                maxHealth: healthStatuses.some((status) => status.maxHealth != null)
                    ? healthStatuses.reduce((total, status) => total + (status.maxHealth || 0), 0)
                    : null
            }
            : { health: null, maxHealth: null };
        if (sourceStatuses.some((status) => status.alive)) {
            return { ...slot, ...healthPatch, alive: true, respawnEndsAt: null, respawnDuration: null };
        }

        const remainingValues = sourceStatuses
            .map((status) => status.respawnRemaining)
            .filter((remaining) => remaining > 0);
        if (remainingValues.length === 0) {
            return { ...slot, ...healthPatch, alive: false, respawnEndsAt: null, respawnDuration: null };
        }

        const remaining = Math.min(...remainingValues);
        const existingEnd = Number(slot.respawnEndsAt);
        const projectedRemaining = Number.isFinite(existingEnd) ? Math.max(0, (existingEnd - now) / 1000) : null;
        const keepExistingEnd = slot.alive === false
            && projectedRemaining != null
            && Math.abs(projectedRemaining - remaining) < 1.5;
        const respawnEndsAt = keepExistingEnd ? existingEnd : now + remaining * 1000;
        const previousDuration = Number(slot.respawnDuration);
        const respawnDuration = slot.alive === false && Number.isFinite(previousDuration)
            ? Math.max(previousDuration, remaining)
            : remaining;

        return { ...slot, ...healthPatch, alive: false, respawnEndsAt, respawnDuration };
    });

    overlayState = { ...state, roster };
    return { ok: true, statusCode: 200, payload: { ok: true, updatedSlots } };
}

function setGameLevel(rawLevel) {
    const level = normalizeLevel(rawLevel);
    if (level == null) {
        return { ok: false, statusCode: 422, payload: { error: "Invalid level", received: rawLevel } };
    }

    overlayState = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        level,
        gameplayVisible: true,
        ...defaultPlayerUpgrades,
        seconds: 0,
        running: true,
        startedAt: Date.now(),
        monsters: [],
        roster: [],
        rosterPending: true
    };

    return { ok: true, statusCode: 200, payload: { ok: true, level } };
}

function setGameplayVisibility(rawVisible) {
    if (typeof rawVisible !== "boolean") {
        return { ok: false, statusCode: 422, payload: { error: "Invalid visibility", received: rawVisible } };
    }

    overlayState = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        gameplayVisible: rawVisible
    };

    return { ok: true, statusCode: 200, payload: { ok: true, gameplayVisible: rawVisible } };
}

function setPlayerStrength(rawStrength) {
    const strength = Number(rawStrength);
    if (!Number.isFinite(strength) || strength < 0) {
        return { ok: false, statusCode: 422, payload: { error: "Invalid strength", received: rawStrength } };
    }

    overlayState = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        strength: Math.floor(strength)
    };

    return { ok: true, statusCode: 200, payload: { ok: true, strength: overlayState.strength } };
}

function setTabHidden(rawHidden) {
    if (typeof rawHidden !== "boolean") {
        return { ok: false, statusCode: 422, payload: { error: "Invalid Tab visibility", received: rawHidden } };
    }

    overlayState = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        tabHidden: rawHidden
    };
    return { ok: true, statusCode: 200, payload: { ok: true, tabHidden: rawHidden } };
}

function setTumbleLaunch(rawTumbleLaunch) {
    const tumbleLaunch = Number(rawTumbleLaunch);
    if (!Number.isFinite(tumbleLaunch) || tumbleLaunch < 0) {
        return { ok: false, statusCode: 422, payload: { error: "Invalid Tumble Launch", received: rawTumbleLaunch } };
    }

    overlayState = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        tumbleLaunch: Math.floor(tumbleLaunch)
    };

    return { ok: true, statusCode: 200, payload: { ok: true, tumbleLaunch: overlayState.tumbleLaunch } };
}

function setPlayerUpgrades(rawUpgrades) {
    if (!rawUpgrades || typeof rawUpgrades !== "object" || Array.isArray(rawUpgrades)) {
        return { ok: false, statusCode: 422, payload: { error: "Invalid player upgrades" } };
    }

    const updates = {};
    for (const key of Object.keys(defaultPlayerUpgrades)) {
        if (!(key in rawUpgrades)) continue;
        const value = Number(rawUpgrades[key]);
        if (!Number.isFinite(value) || value < 0) {
            return { ok: false, statusCode: 422, payload: { error: `Invalid upgrade: ${key}`, received: rawUpgrades[key] } };
        }
        updates[key] = Math.floor(value);
    }

    if (Object.keys(updates).length === 0) {
        return { ok: false, statusCode: 422, payload: { error: "No known player upgrades received" } };
    }

    overlayState = {
        ...defaultOverlayState,
        ...(normalizeOverlayState(overlayState) || {}),
        ...updates
    };
    return { ok: true, statusCode: 200, payload: { ok: true, upgrades: updates } };
}

function serveFile(request, response) {
    const requestUrl = new URL(request.url, `http://${request.headers.host}`);
    const pathname = decodeURIComponent(requestUrl.pathname === "/" ? "/overlay.html" : requestUrl.pathname);
    const filePath = path.normalize(path.join(serverRoot, pathname));

    if (!filePath.startsWith(serverRoot)) {
        response.writeHead(403);
        response.end("Forbidden");
        return;
    }

    fs.stat(filePath, (statError, stats) => {
        if (statError || !stats.isFile()) {
            response.writeHead(404);
            response.end("Not found");
            return;
        }

        const ext = path.extname(filePath).toLowerCase();
        response.writeHead(200, {
            "Content-Type": mimeTypes[ext] || "application/octet-stream",
            "Cache-Control": "no-store"
        });
        fs.createReadStream(filePath).pipe(response);
    });
}

const server = http.createServer(async (request, response) => {
    try {
        if (request.method === "GET" && request.url === "/api/state") {
            sendJson(response, 200, { state: overlayState });
            return;
        }

        if (request.method === "POST" && request.url === "/api/state") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            overlayState = normalizeOverlayState(payload.state);
            sendJson(response, 200, { ok: true });
            broadcastState();
            return;
        }

        if (request.method === "POST" && request.url === "/api/monster-seen") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = addSeenMonster(payload.name || payload.monster || payload.enemy, payload.id ?? payload.instanceId);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok && result.payload.added) {
                broadcastState();
            }
            return;
        }

        if (request.method === "POST" && request.url === "/api/roster") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = updateMonsterRoster(payload.monsters);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok) broadcastState();
            return;
        }

        if (request.method === "POST" && request.url === "/api/monster-status") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = updateMonsterStatuses(payload.statuses);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok && result.payload.updatedSlots > 0) broadcastState();
            return;
        }

        if (request.method === "POST" && request.url === "/api/level") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = setGameLevel(payload.level);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok) {
                broadcastState();
            }
            return;
        }

        if (request.method === "POST" && request.url === "/api/visibility") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = setGameplayVisibility(payload.visible ?? payload.gameplayVisible);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok) {
                broadcastState();
            }
            return;
        }

        if (request.method === "POST" && request.url === "/api/strength") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = setPlayerStrength(payload.strength);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok) {
                broadcastState();
            }
            return;
        }

        if (request.method === "POST" && request.url === "/api/tab-hidden") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = setTabHidden(payload.hidden ?? payload.tabHidden);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok) {
                broadcastState();
            }
            return;
        }

        if (request.method === "POST" && request.url === "/api/tumble-launch") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = setTumbleLaunch(payload.tumbleLaunch ?? payload.value);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok) {
                broadcastState();
            }
            return;
        }

        if (request.method === "POST" && request.url === "/api/upgrades") {
            const body = await readBody(request);
            const payload = JSON.parse(body || "{}");
            const result = setPlayerUpgrades(payload.upgrades ?? payload);
            sendJson(response, result.statusCode, result.payload);
            if (result.ok) {
                broadcastState();
            }
            return;
        }

        if (request.method === "GET" && request.url === "/api/events") {
            response.writeHead(200, {
                "Content-Type": "text/event-stream; charset=utf-8",
                "Cache-Control": "no-store",
                Connection: "keep-alive"
            });
            response.write("\n");
            clients.add(response);
            request.on("close", () => clients.delete(response));
            return;
        }

        if (request.method === "GET") {
            serveFile(request, response);
            return;
        }

        response.writeHead(405);
        response.end("Method not allowed");
    } catch (error) {
        sendJson(response, 500, { error: error.message });
    }
});

function startOverlayServer(options = {}) {
    if (server.listening) return Promise.resolve(server);

    serverRoot = path.resolve(options.root || __dirname);
    const listenHost = options.host || host;
    const listenPort = Number(options.port || port);

    return new Promise((resolve, reject) => {
        const handleError = (error) => reject(error);
        server.once("error", handleError);
        server.listen(listenPort, listenHost, () => {
            server.off("error", handleError);
            console.log(`Overlay: http://${listenHost}:${listenPort}/overlay.html`);
            resolve(server);
        });
    });
}

function stopOverlayServer() {
    for (const client of clients) client.end();
    clients.clear();
    if (!server.listening) return Promise.resolve();
    return new Promise((resolve) => server.close(resolve));
}

if (require.main === module) {
    startOverlayServer().catch((error) => {
        console.error(`Overlay server failed: ${error.message}`);
        process.exitCode = 1;
    });
}

module.exports = { startOverlayServer, stopOverlayServer };
