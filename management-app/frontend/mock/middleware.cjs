function createMockToken(username = 'admin') {
  const header = Buffer.from(JSON.stringify({ alg: 'HS256', typ: 'JWT' })).toString('base64url')
  const payload = Buffer.from(
    JSON.stringify({
      name: username,
      sub: username,
      // Like the real backend's first login, "admin" is the super admin.
      role: username === 'admin' ? 'super_admin' : 'admin',
      exp: Math.floor(Date.now() / 1000) + 365 * 24 * 60 * 60,
      iat: Math.floor(Date.now() / 1000),
    })
  ).toString('base64url')
  const signature = 'mock-signature'

  return `${header}.${payload}.${signature}`
}

function usernameFromRequest(req) {
  try {
    const token = (req.headers.authorization || '').replace(/^Bearer /, '')
    return JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString()).name
  } catch {
    return undefined
  }
}

// Xray profiles, kept in memory. Switching only flips the active flag; nothing restarts.
const xrayProfiles = {
  mode: 'profiles',
  active: 'vless-reality',
  applied: true,
  last_error: null,
  profiles: [
    {
      name: 'my-profile',
      valid: false,
      errors: ['client-config.template still contains YOUR_CF_DOMAIN: run install.sh with --cf-domain.'],
      inbounds: [{ tag: 'my-in', network: 'grpc', security: 'tls', port: '443', flow: '' }],
    },
    {
      name: 'vless-cf-ws',
      valid: true,
      errors: [],
      inbounds: [{ tag: 'vless-cf', network: 'ws', security: 'tls', port: '443', flow: '' }],
    },
    {
      name: 'vless-cf-xhttp',
      valid: true,
      errors: [],
      inbounds: [{ tag: 'vless-xhttp', network: 'xhttp', security: 'tls', port: '443', flow: 'xtls-rprx-vision' }],
    },
    {
      name: 'vless-reality',
      valid: true,
      errors: [],
      inbounds: [{ tag: 'vless-in', network: 'tcp', security: 'reality', port: '443', flow: 'xtls-rprx-vision' }],
    },
  ],
}

function xrayProfilesState() {
  return {
    ...xrayProfiles,
    profiles: xrayProfiles.profiles.map((p) => ({ ...p, active: p.name === xrayProfiles.active })),
  }
}

function activateXrayProfile(res, name) {
  const profile = xrayProfiles.profiles.find((p) => p.name === name)
  if (!profile) return res.status(404).end()
  if (!profile.valid) return res.status(400).json({ errors: profile.errors })
  xrayProfiles.active = name
  return res.json({ active: name })
}

// Note: the mock does not scope users or usage per admin — every admin sees all mock data.
module.exports = function (req, res, next) {
  if (req.method === 'GET' && req.url === '/v1/xray/profiles') {
    return res.json(xrayProfilesState())
  }
  if (req.method === 'PUT' && req.url === '/v1/xray/active-profile') {
    return activateXrayProfile(res, req.body?.name)
  }
  if (req.method === 'POST' && req.url === '/v1/xray/active-profile/reapply') {
    return activateXrayProfile(res, xrayProfiles.active)
  }
  if (req.method === 'POST' && req.url === '/v1/auth/login') {
    const username = req.body?.username || 'admin'
    return res.json({
      access_token: createMockToken(username),
    })
  }
  if (req.method === 'POST' && req.url === '/v1/auth/refresh') {
    const username = req.body?.username || 'admin'
    return res.json({
      access_token: createMockToken(username),
    })
  }
  if (req.method === 'POST' && req.url === '/v1/admins') {
    // The faker keys records by "id"; use the username so DELETE /admins/:username works.
    // A duplicate username makes the faker answer 400 rather than the backend's 409.
    const username = String(req.body?.username || '').trim()
    req.body = { id: username, username, role: 'admin', status: 'pending', user_count: 0 }
  }
  if (req.method === 'POST' && req.url === '/v1/users') {
    req.body = { ...req.body, created_by: usernameFromRequest(req) || 'admin' }
  }
  next()
}
