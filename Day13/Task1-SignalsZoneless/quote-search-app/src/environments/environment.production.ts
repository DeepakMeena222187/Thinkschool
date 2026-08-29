// Overwritten by the deploy workflow immediately before `ng build --configuration
// production` (from a workflow variable, never committed) - kept as an obvious
// placeholder here rather than a real-looking URL so a skipped substitution
// step fails loudly instead of silently building against the wrong API.
export const environment = {
  apiBaseUrl: '__API_BASE_URL__',
};
