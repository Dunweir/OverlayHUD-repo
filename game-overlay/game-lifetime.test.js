const test = require("node:test");
const assert = require("node:assert/strict");
const { parseGamePid, startGameLifetimeWatcher } = require("./game-lifetime");

function watch(argv = ["--overlay-game-pid=1234"], initialError) {
    const state = { probes: [], quits: 0, clears: 0, unrefs: 0, error: initialError };
    const timer = { unref() { state.unrefs++; } };
    state.stop = startGameLifetimeWatcher({
        argv,
        quit() { state.quits++; },
        kill(pid, signal) {
            state.probes.push([pid, signal]);
            if (state.error) throw state.error;
        },
        setInterval(callback, delay) {
            state.tick = callback;
            assert.equal(delay, 1000);
            return timer;
        },
        clearInterval(handle) {
            assert.equal(handle, timer);
            state.clears++;
        }
    });
    return state;
}

test("parses only an explicit positive signed-32-bit game PID", () => {
    assert.equal(parseGamePid(["electron", ".", "--overlay-game-pid=1234"]), 1234);
    assert.equal(parseGamePid(["--overlay-game-pid=2147483647"]), 2147483647);
    for (const value of ["", "0", "-1", "+1", "01", "1.5", "1e3", " 1", "1 ", "123abc", "2147483648", "9007199254740993"]) {
        assert.equal(parseGamePid([`--overlay-game-pid=${value}`]), null, value);
    }
    assert.equal(parseGamePid(["--overlay-game-pid", "1234"]), null);
    assert.equal(parseGamePid(["--overlay-game-pid=1", "--overlay-game-pid=2"]), null);
    assert.equal(parseGamePid(["--overlay-game-pid=1", "--overlay-game-pid"]), null);
});

test("standalone, AutoClose=false and invalid flags create no watcher", () => {
    for (const argv of [[], ["electron", "."], ["--other=1234"], ["--overlay-game-pid=0"], ["--overlay-game-pid=1", "--overlay-game-pid=2"]]) {
        const state = watch(argv);
        state.stop();
        assert.equal(state.tick, undefined);
        assert.deepEqual(state.probes, []);
        assert.equal(state.quits, 0);
        assert.equal(state.clears, 0);
    }
});

test("probes immediately and periodically with signal zero without keeping Node alive", () => {
    const state = watch();
    state.tick();
    assert.deepEqual(state.probes, [[1234, 0], [1234, 0]]);
    assert.equal(state.unrefs, 1);
    assert.equal(state.quits, 0);
    state.stop();
});

test("ESRCH stops polling and quits exactly once", () => {
    const state = watch();
    state.error = Object.assign(new Error("gone"), { code: "ESRCH" });
    state.tick();
    state.tick();
    state.stop();
    assert.equal(state.quits, 1);
    assert.equal(state.clears, 1);
    assert.equal(state.probes.length, 2);
});

test("a game already gone at startup also quits", () => {
    const state = watch(undefined, { code: "ESRCH" });
    assert.equal(state.quits, 1);
    assert.equal(state.clears, 1);
    state.tick();
    assert.equal(state.probes.length, 1);
});

test("other probe errors keep watching until an ESRCH", () => {
    const state = watch();
    for (const error of [{ code: "EPERM" }, { code: "EACCES" }, { code: "EINVAL" }, new Error("unknown"), "unknown"]) {
        state.error = error;
        state.tick();
        assert.equal(state.quits, 0);
        assert.equal(state.clears, 0);
    }
    state.error = null;
    state.tick();
    assert.equal(state.quits, 0);
    state.error = { code: "ESRCH" };
    state.tick();
    assert.equal(state.quits, 1);
});

test("cleanup is idempotent and prevents subsequent probes or quit", () => {
    const state = watch();
    state.stop();
    state.stop();
    state.error = { code: "ESRCH" };
    state.tick();
    assert.equal(state.clears, 1);
    assert.equal(state.probes.length, 1);
    assert.equal(state.quits, 0);
});

test("a relaunched overlay watches the same game, not its previous overlay PID", () => {
    const argv = ["OverlayHUD.exe", "--overlay-game-pid=1234", "--disable-gpu"];
    const original = watch(argv);
    original.stop();
    const relaunched = watch(argv);
    relaunched.error = { code: "ESRCH" };
    relaunched.tick();
    assert.deepEqual(relaunched.probes, [[1234, 0], [1234, 0]]);
    assert.equal(original.quits, 0);
    assert.equal(relaunched.quits, 1);
});
