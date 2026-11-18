/** Export the real UI components as an offline, fictional workspace for visual review. */
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { build } from "vite";
import { compile } from "@tailwindcss/node";
import { Scanner } from "@tailwindcss/oxide";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const out = path.join(root, ".design-preview");
await fs.mkdir(out, { recursive: true });
await fs.writeFile(
  path.join(out, "navigation.tsx"),
  `import { useSyncExternalStore, type ComponentPropsWithoutRef } from 'react';
const subscribe=(cb:()=>void)=>{window.addEventListener('hashchange',cb);return()=>window.removeEventListener('hashchange',cb)};
const snapshot=()=>window.location.hash.slice(1)||'/design-preview';
export const usePathname=()=>useSyncExternalStore(subscribe,snapshot,snapshot).split('?')[0];
export const useSearchParams=()=>new URLSearchParams(useSyncExternalStore(subscribe,snapshot,snapshot).split('?')[1]||'');
export const useRouter=()=>({replace:(url:string)=>{window.location.hash=url},push:(url:string)=>{window.location.hash=url}});
export default function Link({href,children,...props}:ComponentPropsWithoutRef<'a'> & {href:string}){return <a href={'#'+href} {...props}>{children}</a>}`,
);
await fs.writeFile(
  path.join(out, "entry.tsx"),
  `import React from 'react';import {createRoot} from 'react-dom/client';import {Suspense} from 'react';import {PreviewWorkspace} from '../features/showcase/design-preview/preview-workspace';import {LoginView} from '../features/auth/components/login-view';import {Toaster} from 'sonner';import {usePathname} from './navigation';
function App(){const p=usePathname();return <><Suspense fallback={null}>{p==='/login'?<><div className="gh-preview-banner">SIGN-IN DESIGN PREVIEW · Identity provider requires the running application.<a href="#/design-preview">Open workspace preview ↗</a></div><LoginView/></>:<PreviewWorkspace/>}</Suspense><Toaster richColors/></>};
document.addEventListener('submit',e=>{if((e.target as HTMLFormElement).action.includes('/auth/login')){e.preventDefault();window.alert('This offline preview does not sign you in. The application sends your email to Keycloak as a login hint.');}});
createRoot(document.getElementById('root')!).render(<App/>);`,
);
const compiler = await compile(await fs.readFile(path.join(root, "app/globals.css"), "utf8"), {
  base: path.join(root, "app"),
  onDependency: () => {},
});
const scanner = new Scanner({ sources: [{ base: root, pattern: "{app,components,features}/**/*.{tsx,ts}", negated: false }] });
const css =
  compiler.build(scanner.scan()) +
  "\n" +
  (await fs.readFile(path.join(root, "app/design.css"), "utf8")) +
  "\n" +
  (await fs.readFile(path.join(root, "app/security-design.css"), "utf8"));
await build({
  root,
  configFile: false,
  logLevel: "error",
  resolve: {
    alias: [
      { find: "@", replacement: root },
      { find: "next/navigation", replacement: path.join(out, "navigation.tsx") },
      { find: "next/link", replacement: path.join(out, "navigation.tsx") },
    ],
  },
  define: { "process.env.NODE_ENV": '"production"' },
  build: {
    outDir: path.join(out, "dist"),
    emptyOutDir: true,
    minify: true,
    lib: { entry: path.join(out, "entry.tsx"), formats: ["iife"], name: "GatehouseDesign", fileName: () => "preview.js" },
  },
});
const js = await fs.readFile(path.join(out, "dist/preview.js"), "utf8");
const html =
  '<!doctype html><html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Gatehouse — Design Workspace</title><style>' +
  css +
  '</style></head><body><div id="root"></div><script>' +
  js.replaceAll("</script", "<\\/script") +
  "</script></body></html>";
const target = path.join(out, "gatehouse-design-preview.html");
await fs.writeFile(target, html);
console.log(target);
