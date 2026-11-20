// @module: Housekeeping.Web/utils/subpathUrl
// @purpose: Detects Mother /app/<appId> reverse-proxy prefixes and resolves relative API, SignalR, and PWA asset URLs safely.
// @depends-on: (none — pure browser location helpers)
// @exports: detectAppBasePrefix, resolveAppUrl, resolveApiV1Url, parseQrSvgMarkup

export interface ParsedQrSvg {
  viewBox: string;
  size: number;
  lightColor: string;
  darkColor: string;
  pathData: string;
}

const APP_SUBPATH_REGEX = /^(\/app\/[a-z0-9_-]+)(?:\/|$)/i;

export function detectAppBasePrefix(pathname?: string): string {
  const raw =
    pathname ??
    (typeof window !== 'undefined' && window.location ? window.location.pathname : '/');
  const match = APP_SUBPATH_REGEX.exec(raw);
  return match ? match[1] : '';
}

export function resolveAppUrl(targetPath: string): string {
  const prefix = detectAppBasePrefix();
  const clean = targetPath.replace(/^\.?\/*/, '');
  if (!clean) {
    return prefix ? `${prefix}/` : '/';
  }
  return `${prefix}/${clean}`;
}

export function resolveApiV1Url(apiPath: string): string {
  const clean = apiPath.replace(/^\/+/, '');
  return resolveAppUrl(`api/v1/${clean}`);
}

export function parseQrSvgMarkup(svgMarkup?: string | null): ParsedQrSvg | null {
  if (!svgMarkup) return null;
  const viewBoxMatch = /viewBox="([^"]+)"/i.exec(svgMarkup);
  const rectFillMatch = /<rect[^>]*fill="([^"]+)"/i.exec(svgMarkup);
  const pathFillMatch = /<path[^>]*fill="([^"]+)"/i.exec(svgMarkup);
  const pathDMatch = /<path[^>]*\bd="([^"]+)"/i.exec(svgMarkup);
  if (!viewBoxMatch || !pathDMatch) return null;

  const viewBox = viewBoxMatch[1];
  const nums = viewBox.trim().split(/\s+/);
  const size = Number(nums[2]) || 41;
  return {
    viewBox,
    size,
    lightColor: rectFillMatch ? rectFillMatch[1] : '#ffffff',
    darkColor: pathFillMatch ? pathFillMatch[1] : '#0b0e14',
    pathData: pathDMatch[1],
  };
}
