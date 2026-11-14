import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router';
import { createServer } from 'vite';

const server = await createServer({
  root: fileURLToPath(new URL('../', import.meta.url)),
  server: { middlewareMode: true, hmr: false, ws: false, watch: null },
  plugins: [{ name: 'mock-error-session', enforce: 'pre', load(id) {
    if (id.endsWith('/context/AuthContext.jsx')) return 'export function useAuth() { return globalThis.__errorRecoveryAuth; }';
  } }],
});
try {
  const { default: ErrorPage } = await server.ssrLoadModule('/src/pages/errors/ErrorPage.jsx');
  const { default: ProtectedRoute } = await server.ssrLoadModule('/src/routes/ProtectedRoute.jsx');
  const render = (element) => renderToStaticMarkup(createElement(MemoryRouter, null, element));
  for (const code of [403, 404, 500, 503, 999]) for (const embedded of [false, true]) {
    for (const status of ['authenticated', 'anonymous', 'loading', 'error']) {
      globalThis.__errorRecoveryAuth = { status, isAuthenticated: status === 'authenticated', retrySession() {} };
      const html = render(createElement(ErrorPage, { code, embedded }));
      if (status === 'authenticated') assert.match(html, /href="\/dashboard"/);
      if (status === 'anonymous') assert.match(html, /href="\/"/);
      if (status === 'loading' || status === 'error') {
        assert.doesNotMatch(html, /<a\s/);
        assert.match(html, status === 'loading' ? /Verificando sessão/ : /Verificar sessão novamente/);
      }
    }
  }
  globalThis.__errorRecoveryAuth = { status: 'error', sessionError: 503, retrySession() {} };
  assert.doesNotMatch(render(createElement(ProtectedRoute, null, 'private content')), /private content|<a\s/);
  globalThis.__errorRecoveryAuth = { status: 'authenticated', isAuthenticated: true };
  assert.match(render(createElement(ProtectedRoute, null, 'private content')), /private content/);
  const { apiFetch, setCsrfToken } = await server.ssrLoadModule('/src/api/client.js');
  const originalFetch = globalThis.fetch;
  const originalWindow = globalThis.window;
  try {
    globalThis.window = { location: { pathname: '/dashboard', href: '/dashboard' } };
    setCsrfToken('test-csrf');
    for (const status of [403, 404, 408, 429, 500, 503]) {
      globalThis.fetch = async () => new Response('{}', { status });
      await assert.rejects(apiFetch('/transactions'), (error) => error.status === status);
      assert.equal(globalThis.window.location.href, '/dashboard');
    }
    globalThis.fetch = async () => { throw new TypeError('offline'); };
    await assert.rejects(apiFetch('/transactions'), (error) => error.status === 0);
    assert.equal(globalThis.window.location.href, '/dashboard');
    globalThis.fetch = async (_url, options) => {
      assert.equal(options.credentials, 'include');
      assert.equal(options.headers['X-CSRF-Token'], 'test-csrf');
      return new Response('{}');
    };
    await apiFetch('/transactions', { method: 'POST', body: '{}' });
    globalThis.fetch = async () => new Response('{}', { status: 401 });
    await assert.rejects(apiFetch('/auth/me'), (error) => error.status === 401);
    assert.equal(globalThis.window.location.href, '/dashboard');
    await assert.rejects(apiFetch('/transactions'), (error) => error.status === 401);
    assert.equal(globalThis.window.location.href, '/loginemail');
  } finally {
    globalThis.fetch = originalFetch;
    globalThis.window = originalWindow;
  }
  console.log('Passed: 40 error/session combinations, protected routes, network failures, CSRF and 401 handling.');
} finally {
  delete globalThis.__errorRecoveryAuth;
  await server.close();
}
