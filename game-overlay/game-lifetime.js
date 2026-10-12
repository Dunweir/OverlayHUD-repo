const GAME_PID_FLAG = "--overlay-game-pid";

function parseGamePid(argv) {
    const flags = argv.filter((arg) => arg === GAME_PID_FLAG || arg.startsWith(`${GAME_PID_FLAG}=`));
    // Ambiguous or malformed ownership must never close a standalone overlay.
    if (flags.length !== 1) return null;
    const value = flags[0].slice(GAME_PID_FLAG.length + 1);
    if (!/^[1-9]\d*$/.test(value)) return null;
    const pid = Number(value);
    // Node accepts a signed 32-bit PID; zero and negatives target process groups.
    return Number.isSafeInteger(pid) && pid <= 2147483647 ? pid : null;
}

function startGameLifetimeWatcher({
    quit,
    argv = process.argv,
    kill = process.kill.bind(process),
    setInterval = global.setInterval,
    clearInterval = global.clearInterval
}) {
    const pid = parseGamePid(argv);
    if (pid === null) return () => {};

    let stopped = false;
    let timer;
    function stop() {
        if (stopped) return;
        stopped = true;
        clearInterval(timer);
    }
    function check() {
        if (stopped) return;
        try {
            kill(pid, 0);
        } catch (error) {
            // Access denied and other probe failures do not prove the game exited.
            if (error?.code !== "ESRCH") return;
            stop();
            quit();
        }
    }

    timer = setInterval(check, 1000);
    timer.unref();
    // Also handle relaunch completing after the game has already exited.
    check();
    return stop;
}

module.exports = { parseGamePid, startGameLifetimeWatcher };
