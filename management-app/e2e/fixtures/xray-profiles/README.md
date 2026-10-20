Xray profiles used by the e2e tests and the CI e2e job (mounted as `Xray__ProfilesDir`).

- `fixture-reality` — valid, the default profile.
- `fixture-ws` — valid; switching to it changes every config link to `type=ws`.
- `fixture-broken` — still contains `YOUR_PRIVATE_KEY`, so it can't be activated.
