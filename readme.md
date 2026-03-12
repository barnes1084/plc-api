# gme-dm-plc-api

Here’s a clean way to think about this system as a **pipeline**, and then the **use-cases** that fall out of it.

## The big idea

Your `TagDefinition` is the “single source of truth” for:

* **What** to read/write (`Address`, `DataType`)
* **How** to reach it (`DriverType`, `Ip`, `Path`)
* **How** it behaves (`Mode`, `PollMs`, `Enabled`)

Everything else exists to:

1. store these definitions,
2. decide *when* to poll vs *when* to do on-demand,
3. perform the read/write via the right driver,
4. publish results (Redis cache, Influx trend, direct response).

---

## Sequence: what happens at runtime

### 1) Startup

**Program.cs** wires DI + hosted services.

* `TagRegistry` loads tag definitions (today maybe in-memory/appsettings; later from a DB or JSON file).
* `TagCache` connects to Redis (or an in-memory cache if Redis is off).
* `DashboardPoller` starts running in the background.

### 2) TagRegistry = “configuration brain”

Responsibilities:

* Store all `TagDefinition`s
* Allow CRUD: add/update/delete tags, enable/disable tags
* Provide fast lookup: by Id, by Mode, by DriverType, by IP, etc.

This is where your “end-user configuration UI” will write changes.

### 3) DashboardPoller = “automatic polling engine”

Loop:

* Ask `TagRegistry`: “give me all Enabled tags with Mode=Dashboard”
* Group them smartly (more on that below)
* For each tag due to poll:

  * Know which driver to use (`DriverType`)
  * Request a connection from `PlcConnectionManager`
  * Read tag value using driver (`EIP.ReadDint / ReadBool`, later Modbus read)
  * Store latest value in `TagCache` (Redis)
  * (Optional) update health/quality: last success time, error count, latency, etc.

### 4) PlcConnectionManager = “connection lifecycle + rate limiting”

This is the piece that protects the PLC Ethernet card.

Instead of “connect → read → dispose” for every web request:

* Maintain a small pool keyed by `(DriverType, Ip, Path)`
* Reuse an existing connection if it’s still healthy
* Limit concurrent requests per PLC
* Optionally enforce “minimum time between reads per PLC” or per tag group
* Auto-reconnect if a socket drops

### 5) Controllers = “HTTP API surface”

Controllers should be thin and call services.

Typical controller flow:

* Validate input (tag id / address / value)
* Decide which route is being used:

  * dashboard read (cached)
  * command read (direct)
  * write command
  * manage tags (CRUD)
* Call the correct service method
* Return DTO response

---

## What are all the use cases?


# A) Live Dashboard (polled + cached)

**Goal:** lots of clients can refresh fast without touching PLC.

**Flow**

1. User opens Angular dashboard
2. Angular calls API: “give me the latest values for these tag IDs”
3. API returns from `TagCache` (Redis) immediately
4. Poller keeps Redis fresh on its own schedule

**Benefits**

* PLC load is stable and predictable
* 1,000 web clients don’t create 1,000 PLC connections
* Response time is fast

**Extra dashboard use cases**

* Dashboard loads tag list + metadata (units, display name, min/max)
* “show stale” if last update older than X seconds
* “tag health” indicator (online/offline/last error)

---

# B) Trending (polled + stored in InfluxDB)

**Goal:** continuous time-series storage, independent of dashboard usage.

**Flow**

1. Poller asks registry for tags with `Mode=Trending`
2. Poller reads on `PollMs` or a fixed schedule
3. Poller writes points to InfluxDB (timestamp + value + tags)
4. Dashboard graphs query Influx (or you proxy via API)

**Key design decision**
Trending poll interval is often *slower* (e.g., 1s/5s/10s) and separate from dashboard refresh needs.

**Extra trending use cases**

* Downsample / aggregate (store 1s raw, keep 1m averages)
* Deadband (only write if value changed by X)
* Alarm events (send notifications on condition)

---

# C) Command/Parameters (not polled, on-demand read/write)

**Goal:** operator actions, tuning, “set this value now”.

**Flow (write)**

1. Angular calls API: “write tag X = value”
2. API uses `PlcConnectionManager` to get connection
3. Driver writes (`WriteDint/WriteBool`)
4. Optional: read back to confirm
5. API responds success/failure

**Flow (read-on-demand)**
Same idea, but read immediately.

**Important**
Commands should usually bypass Redis and go direct, *but* you can optionally update Redis after successful write so dashboard reflects it immediately.

**Extra command use cases**

* “write with verify” (write then read back)
* “write requires role/permission” (admin only)
* “write with bounds validation” (0–1000 only)
* “command queue” if PLC or connection is busy

---

## How DriverType fits in

`DriverType` means your app becomes “driver-agnostic” at the higher level.

In practice you’ll want a simple factory/adapter like:

* `IPlcDriver` interface: `ReadInt32`, `WriteInt32`, `ReadBool`, `WriteBool`
* `EipDriver : IPlcDriver` wraps your `EIP` class
* `ModbusTcpDriver : IPlcDriver` later

Then Poller/Controllers don’t care which protocol—only the TagDefinition does.

---

## The “grouping” concept (massive for scaling)

As soon as you have lots of tags, you’ll want to group reads to reduce overhead.

At minimum:

* Group by `(DriverType, Ip, Path)`

So the poller does:

* Connect once per group
* Read N tags on that connection
* Cache N results

Later improvements:

* Batch reads (EIP supports multi-service / multi-request patterns)
* Prioritize “fast” groups vs “slow” groups
* Rate limit per PLC

---

## Summary: what happens in order (simple mental model)

1. **TagRegistry** defines what exists and how it should be handled
2. **DashboardPoller** continuously updates **TagCache** for dashboard + trending storage
3. **Controllers** serve:

   * dashboard reads from cache (cheap)
   * trending from Influx (or via API)
   * commands direct to PLC (controlled + limited)
4. **PlcConnectionManager** protects PLCs by reusing connections + limiting concurrency
5. **DriverType** chooses the driver implementation behind a common interface

---


# Installation
Below is a clean, copy/paste setup for a **systemd service** to run your published ASP.NET API on Linux, assuming you will deploy to:

`/users/api/plc-api/`

(inside that folder will be your published output from the ZIP)



## 1) Run beroot

```bash
beroot
<enter password>
```

## 2) Copy + unzip your release ZIP

From your local machine (example using scp):

```bash
scp plc-api-linux-x64.zip youruser@YOUR_SERVER_IP:/tmp/
```

On the Linux VM:

```bash
mkdir -p /users/aps/plc-api
unzip -o /users/aps/plc-api-linux-x64.zip -d /users/aps/plc-api
chmod +x /users/aps/plc-api
chmod +x /users/aps/plc-api/linux-x64
chmod +x /users/aps/plc-api/linux-x64/plc-api.dll
```

### Confirm what got extracted

```bash
ls -lah /users/aps/plc-api/linux-x64
```

You should see:

* a `plc-api.dll`
This will be what we run.


## 3) Create `plc_api.service`

Create the file:

```bash
vi /etc/systemd/system/plc_api.service
```

Paste this (edit the `ExecStart` line depending on what you have):

```ini
[Unit]
Description=PLC API (plc-api)
After=network.target

[Service]
Type=simple
User=appmgr
Group=appmgr
WorkingDirectory=/users/aps/plc-api/linux-x64

# Kestrel bind address/port
Environment=ASPNETCORE_URLS=http://0.0.0.0:5080
Environment=ASPNETCORE_ENVIRONMENT=Production

# Start the app
ExecStart=/usr/bin/dotnet /users/aps/plc-api/linux-x64/plc-api.dll

Restart=always
RestartSec=5

# Logging
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
```


## 4) Enable + start the service

```bash
systemctl daemon-reload
systemctl enable plc_api.service
systemctl start plc_api.service
```

Check status:

```bash
systemctl status plc_api.service --no-pager
```

## 5) View logs (journalctl)

Live tail logs:

```bash
journalctl -u plc_api.service -f
```

Last 200 lines:

```bash
journalctl -u plc_api.service -n 200 --no-pager
```

## 6) Confirm it’s listening

If you set port `5080`:

```bash
ss -lntp | grep 5080
```

Test locally on the VM:

```bash
curl -I http://localhost:5080
```


## 7) Swagger URL

Swagger is typically at:

* `http://<server-ip>:5080/swagger`
  or sometimes:
* `http://<server-ip>:5080/swagger/index.html`

So from your PC browser:

`http://YOUR_SERVER_IP:5080/swagger`


## 8) Firewall note (common gotcha)

If your Linux firewall blocks inbound ports:

### Firewalld (common on Rocky/CentOS)

```bash
firewall-cmd --list-ports
firewall-cmd --add-port=5080/tcp --permanent
firewall-cmd --reload
```


## Quick checklist

1. ZIP unzipped into: `/users/aps/plc-api`
2. Service file at: `/etc/systemd/system/plc_api.service`
3. `ExecStart` points to the right file (`plc-api.dll`)
4. Service running: `systemctl status plc_api.service`
5. Swagger reachable: `http://server-ip:5080/swagger`


