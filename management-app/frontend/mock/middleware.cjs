function createMockToken(username = 'admin') {
  const header = Buffer.from(JSON.stringify({ alg: 'HS256', typ: 'JWT' })).toString('base64url')
  const payload = Buffer.from(
    JSON.stringify({
      name: username,
      sub: username,
      exp: Math.floor(Date.now() / 1000) + 365 * 24 * 60 * 60,
      iat: Math.floor(Date.now() / 1000),
    })
  ).toString('base64url')
  const signature = 'mock-signature'

  return `${header}.${payload}.${signature}`
}

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
  next()
}
