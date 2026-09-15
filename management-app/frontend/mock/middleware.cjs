module.exports = function (req, res, next) {
  if (req.method === 'POST' && req.url === '/v1/auth/login') {
    return res.json({
      access_token: 'mock-access-token',
    })
  }
  if (req.method === 'POST' && req.url === '/v1/auth/refresh') {
    return res.json({
      access_token: 'mock-access-token',
    })
  }
  next()
}
