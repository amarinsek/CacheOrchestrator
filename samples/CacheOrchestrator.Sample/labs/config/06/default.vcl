vcl 4.1;

import std;
import xkey;

backend default {
    .host = "origin";
    .port = "8080";
}

sub vcl_recv {
    if (req.method == "PURGE" && req.url == "/cache-orchestrator/purge") {
        if (req.http.X-CacheOrchestrator-Key != "dev-edge-key") {
            return (synth(403, "Forbidden"));
        }
        if (!req.http.xkey-purge) {
            return (synth(400, "Missing xkey-purge"));
        }
        set req.http.n-gone = xkey.purge(req.http.xkey-purge);
        return (synth(200, "Invalidated"));
    }

    if (req.method != "GET" && req.method != "HEAD") {
        return (pass);
    }
}

sub vcl_backend_response {
    if (beresp.http.X-CacheOrchestrator-Edge-Cacheable == "1") {
        set beresp.ttl = std.duration(
            beresp.http.X-CacheOrchestrator-Edge-Ttl + "s", 0s);
        if (beresp.http.X-CacheOrchestrator-Edge-Grace) {
            set beresp.grace = std.duration(
                beresp.http.X-CacheOrchestrator-Edge-Grace + "s", 0s);
        }
    } else {
        set beresp.uncacheable = true;
        set beresp.ttl = 0s;
    }

    unset beresp.http.X-CacheOrchestrator-Edge-Cacheable;
    unset beresp.http.X-CacheOrchestrator-Edge-Ttl;
    unset beresp.http.X-CacheOrchestrator-Edge-Grace;
}

sub vcl_deliver {
    if (req.method != "GET" && req.method != "HEAD") {
        set resp.http.Cache-Status = "Varnish; fwd=method";
    } else if (obj.hits > 0 && obj.ttl < 0s) {
        set resp.http.Cache-Status = "Varnish; hit; detail=stale";
    } else if (obj.hits > 0) {
        set resp.http.Cache-Status = "Varnish; hit";
    } else {
        set resp.http.Cache-Status = "Varnish; fwd=uri-miss";
    }
    unset resp.http.xkey;
}
