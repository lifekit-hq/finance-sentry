// Prod environment — served from the nginx container in docker-compose.prod.yml.
// nginx reverse-proxies /api/* to the api container on the finance-sentry bridge
// network, so the browser hits same-origin /api/v1/... and SameSite=Strict
// cookies survive.
export const environment = {
  production: true,
  apiBaseUrl: '/api/v1',
  apiVersion: 'v1',
  wsUrl: '/ws',
};
