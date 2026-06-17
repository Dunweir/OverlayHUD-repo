const http = require("http");
const fs = require("fs");
const path = require("path");

const host = "127.0.0.1";
const port = Number(process.env.PORT || 8787);
const root = __dirname;
const clients = new Set();

let overlayState = null;

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

function serveFile(request, response) {
    const requestUrl = new URL(request.url, `http://${request.headers.host}`);
    const pathname = decodeURIComponent(requestUrl.pathname === "/" ? "/control.html" : requestUrl.pathname);
    const filePath = path.normalize(path.join(root, pathname));

    if (!filePath.startsWith(root)) {
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
            overlayState = payload.state || null;
            sendJson(response, 200, { ok: true });
            broadcastState();
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

server.listen(port, host, () => {
    console.log(`Overlay server: http://${host}:${port}/control.html`);
    console.log(`OBS overlay:     http://${host}:${port}/overlay.html`);
});
