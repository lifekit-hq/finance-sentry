// Prometheus exposition for the frontend's nginx (platform contract, spec 048).
//
// The checker probes `/metrics` on the container's own port, so a sidecar exporter cannot answer
// for it. This turns nginx's stub_status text into the same series the official
// nginx-prometheus-exporter publishes, via an internal subrequest.
//
//   Active connections: 2
//   server accepts handled requests
//    10 10 20
//   Reading: 0 Writing: 1 Waiting: 1

const ACTIVE = /Active connections:\s+(\d+)/;
const TOTALS = /\n\s*(\d+)\s+(\d+)\s+(\d+)\s*\n/;
const STATES = /Reading:\s+(\d+)\s+Writing:\s+(\d+)\s+Waiting:\s+(\d+)/;

function series(name, help, type, value) {
    return `# HELP ${name} ${help}\n# TYPE ${name} ${type}\n${name} ${value}\n`;
}

async function metrics(r) {
    let reply;
    try {
        reply = await r.subrequest('/stub_status');
    } catch (e) {
        r.return(503, `stub_status subrequest failed: ${e}\n`);
        return;
    }
    const active = ACTIVE.exec(reply.responseText);
    const totals = TOTALS.exec(reply.responseText);
    const states = STATES.exec(reply.responseText);
    if (reply.status !== 200 || !active || !totals || !states) {
        r.return(503, `stub_status unavailable (${reply.status})\n`);
        return;
    }

    const body =
        series('nginx_up', 'Status of the last stub_status read (1 = ok).', 'gauge', 1) +
        series('nginx_connections_active', 'Active client connections.', 'gauge', active[1]) +
        series('nginx_connections_reading', 'Connections where nginx is reading the request header.', 'gauge', states[1]) +
        series('nginx_connections_writing', 'Connections where nginx is writing the response back to the client.', 'gauge', states[2]) +
        series('nginx_connections_waiting', 'Idle client connections.', 'gauge', states[3]) +
        series('nginx_connections_accepted_total', 'Accepted client connections.', 'counter', totals[1]) +
        series('nginx_connections_handled_total', 'Handled client connections.', 'counter', totals[2]) +
        series('nginx_http_requests_total', 'Total HTTP requests.', 'counter', totals[3]);

    r.headersOut['Content-Type'] = 'text/plain; version=0.0.4; charset=utf-8';
    r.return(200, body);
}

export default { metrics };
