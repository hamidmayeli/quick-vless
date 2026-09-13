# Xray-Core User Management System Requirements

This document outlines the functional and technical specifications for a lightweight, file-backed User Management System (ASP.NET Minimal API) for [Xray-core](https://github.com/XTLS/Xray-core). The system manages user lifecycles, bandwidth quotas, periodic usage logging, and connections count limit enforcement.


---

## 1. Storage Architecture

To avoid external database dependencies (e.g., MySQL, PostgreSQL), the system relies entirely on flat files.

### 1.1 User Registry (`users.json`)
Stores user identities, proxy settings, quota limits, and current connection states.

```json
{
  "userA": {
    "uuid": "a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b5c6d", // remains the same and used for public endpoint
    "secret": "a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b1234", // can be modified based on needs and used in x-ray config
    "quota": 25000000,
    "expiresOn": "2026-12-12T00:00:00Z",
    "single_connection": false,
    "enabled": true
  },
  "userB": {
    "uuid": "b2c3d4e5-f6a7-4b9c-8d1e-2f3a4b5c6d7e",
    "secret": "a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b1235",
    "quota": 250000000,
    "expiresOn": null,
    "single_connection": true,
    "enabled": true
  },
  "userC": {
    "uuid": "c3d4e5f6-a7b8-4c0d-9e2f-3a4b5c6d7e8f",
    "secret": "a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b1236",
    "quota": null,
    "expiresOn": null,
    "single_connection": true,
    "enabled": true
  }
}

```

*Note: `quota: null` signifies unlimited traffic.*

### 1.2 Usage Log (`usage_history/20260913.csv`)

Appends periodic byte-count snapshots to calculate consumption without modifying historical entries.

```text
User_Name,Uplink_Bytes,Downlink_Bytes,Total_Bytes
user name,52428800,1048576000,1101004800
user name,55000000,1100000000,1155000000

```

### 1.3 Admin Registry (`admins.json`)

Stores the api users (AKA admins)

```json
{
    "admin": {
        "password": {
            "hash": "XXXXXX",
            "salt": "xxxxxxx"
        },
        "refreshTokens": [{
            "token": "c3d4e5f6-a7b8-4c0d-9e2f-3a4b5c6d7e8f",
            "expiry": ....
        }]
    }
}
```

---

## 2. Core Modules & Responsibilities

### 2.1 Administration and access

* **Public config endpoint**: It returns a the config for the asking user
    ```
    vless://a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b5c6d@YOUR_SERVER_IP:443?security=reality&encryption=none&pbk=YOUR_PUBLIC_KEY&headerType=none&fp=chrome&type=tcp&flow=xtls-rprx-vision&sni=dl.google.com&sid=YOUR_SHORT_ID#UserA-Server1
    ```
* **User management endpoints**: The CRUD endpoints for users 
* **Authentication endpoints**: Login and refresh token and other required endpoints
* **Usage report**: returning the usage users

### 2.2 Configuration Management

* **Startup & Sync:** Reads `users.json` and updates Xray's `/usr/local/etc/xray/config.json` inbound `clients` list.
* **Hot-Reloading:** Uses Xray's gRPC Handler API (`xray api command HandlerService`) to dynamically add or remove user credentials at runtime without restarting the service or severing existing connections.

The sample config for VLESS + TCP + REALITY + Vision

```json
{
  "log": {
    "loglevel": "warning"
  },
  "stats": {},
  "api": {
    "services": ["StatsService"],
    "tag": "api"
  },
  "policy": {
    "levels": {
      "0": {
        "statsUserUplink": true,
        "statsUserDownlink": true
      }
    },
    "system": {
      "statsInboundUplink": true,
      "statsInboundDownlink": true
    }
  },
  "inbounds": [
    {
      "listen": "0.0.0.0",
      "port": 443,
      "protocol": "vless",
      "settings": {
        "clients": [
          {
            "id": "a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b1234",
            "flow": "xtls-rprx-vision",
            "email": "userA",
            "level": 0
          },
          {
            "id": "a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b1235",
            "flow": "xtls-rprx-vision",
            "email": "userB",
            "level": 0
          },
          {
            "id": "a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b1236",
            "flow": "xtls-rprx-vision",
            "email": "userC",
            "level": 0
          }
        ],
        "decryption": "none"
      },
      "streamSettings": {
        "network": "tcp",
        "security": "reality",
        "realitySettings": {
          "show": false,
          "dest": "dl.google.com:443",
          "xver": 0,
          "serverNames": [
            "dl.google.com"
          ],
          "privateKey": "YOUR_PRIVATE_KEY_HERE",
          "shortIds": [
            "YOUR_SHORT_ID_HERE"
          ]
        }
      },
      "tag": "vless-inbound"
    }
  ],
  "outbounds": [
    {
      "protocol": "freedom",
      "tag": "direct"
    },
    {
      "protocol": "blackhole",
      "tag": "block"
    }
  ]
}
```

### 2.3 Metrics Collection hosted service

* **Execution Interval:** Configurable loop (default: `300` seconds / 5 minutes).
* **Data Retrieval:** Queries Xray's `StatsService` API via CLI (`xray api statsquery --server=127.0.0.1:10085`).
* **Data Transformation:** Converts byte values to Gigabytes (`bytes / 1073741824`) and appends snapshot records to `usage_history/*.csv`.

### 2.4 Bandwidth Quota Enforcement

1. Calculates cumulative usage per user from history.
2. Compares total usage against `quota` defined in `users.json`.
3. If `Total GB >= Quota GB` (and `quota is not null`):
    * Sets `"enabled": false` in `users.json`.
    * Removes user from Xray via gRPC API (`RemoveUser`).
    * Logs a revocation event.


### 2.5 Single-Connection (Kick Old Connection) Handler

When user reaches the public endpoint to get the config, if it's single connection, the `secret` should be updated and it should be replaced in the x-ray config.

---
