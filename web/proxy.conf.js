// Aspire injects the api's URL; the browser sees one origin, so SameSite=Strict cookies work (spec §8).
const target = process.env['services__api__http__0'] ?? 'http://localhost:5062';
module.exports = { '/api': { target, secure: false, changeOrigin: true } };