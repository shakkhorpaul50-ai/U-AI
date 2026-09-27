/**
 * U-AI public proxy: stable workers.dev URL in front of a rotating trycloudflare tunnel.
 *
 * Why this file exists: quick tunnels hand out a random subdomain on every
 * restart. This Worker is the permanent address; only its UPSTREAM_URL secret
 * changes. Set it with:
 *
 *   echo https://xxx.trycloudflare.com | wrangler secret put UPSTREAM_URL
 *
 * SSE passes through untouched because we return the upstream ReadableStream
 * directly — no buffering, no re-chunking. The app already sends
 * `Content-Type: text/event-stream`, which Cloudflare forwards unbuffered.
 */
export default {
  async fetch(request, env) {
    const upstream = (env.UPSTREAM_URL || "").replace(/\/+$/, "");
    if (!upstream) {
      return new Response("U-AI upstream not configured.", {
        status: 502,
        headers: { "content-type": "text/plain; charset=utf-8" },
      });
    }

    const url = new URL(request.url);
    const target = upstream + url.pathname + url.search;

    const headers = new Headers(request.headers);
    headers.delete("host");
    const ip = request.headers.get("cf-connecting-ip");
    if (ip) headers.set("x-forwarded-for", ip);

    let resp;
    try {
      resp = await fetch(target, {
        method: request.method,
        headers,
        body: ["GET", "HEAD"].includes(request.method) ? undefined : request.body,
        redirect: "manual",
      });
    } catch (e) {
      return new Response(
        "<!DOCTYPE html><html><body style=\"background:#0d0d0d;color:#ececec;font-family:sans-serif;text-align:center;padding-top:15vh\">" +
        "<h1>U-AI is offline</h1><p>The home server is unreachable. Try again later.</p></body></html>",
        { status: 502, headers: { "content-type": "text/html; charset=utf-8" } }
      );
    }

    const out = new Headers(resp.headers);
    out.set("x-accel-buffering", "no");
    return new Response(resp.body, { status: resp.status, headers: out });
  },
};
