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

// Note: the mock does not scope users or usage per admin — every admin sees all mock data.
module.exports = function (req, res, next) {
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
