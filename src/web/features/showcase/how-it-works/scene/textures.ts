import * as THREE from "three";
import type { ApproverScreen, MapScreen } from "./story";

/** Fixed access-state colours (ADR-007, globals.css --gh-state-*); canvas textures cannot read CSS variables. */
export const STATE_COLOR: Record<MapScreen, string> = {
  HasAccess: "#15803d",
  NoAccess: "#b91c1c",
  Pending: "#b45309",
};
const FONT = "system-ui, -apple-system, Segoe UI, sans-serif";

function canvasTexture(w: number, h: number, draw: (c: CanvasRenderingContext2D) => void): THREE.CanvasTexture {
  const canvas = document.createElement("canvas");
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext("2d");
  if (ctx !== null) {
    draw(ctx);
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = 4;
  return texture;
}

function hatch(c: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, color: string) {
  c.save();
  c.beginPath();
  c.rect(x, y, w, h);
  c.clip();
  c.strokeStyle = color;
  c.lineWidth = 4;
  for (let i = -h; i < w; i += 14) {
    c.beginPath();
    c.moveTo(x + i, y + h);
    c.lineTo(x + i + h, y);
    c.stroke();
  }
  c.restore();
}

function appChrome(c: CanvasRenderingContext2D, w: number, h: number, title: string) {
  c.fillStyle = "#f8fafc";
  c.fillRect(0, 0, w, h);
  c.fillStyle = "#0f172a";
  c.fillRect(0, 0, w, 40);
  c.fillStyle = "#fff";
  c.font = `600 20px ${FONT}`;
  c.fillText("Gatehouse", 16, 27);
  c.fillStyle = "#94a3b8";
  c.font = `16px ${FONT}`;
  c.fillText(title, 130, 27);
}

/** Floor plan of the corridor in the scene: three rooms on one side, the lab coloured by Anna's access state. */
function floorPlan(c: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, lab: MapScreen | null) {
  const roomW = w / 3;
  const roomH = h * 0.62;
  ["2.01 Meeting", "2.04 Lab", "2.06 Comms"].forEach((name, i) => {
    const rx = x + i * roomW;
    const isLab = i === 1 && lab !== null;
    c.fillStyle = isLab ? `${STATE_COLOR[lab]}33` : "#e2e8f0";
    c.fillRect(rx, y, roomW, roomH);
    if (isLab && lab === "Pending") {
      hatch(c, rx, y, roomW, roomH, `${STATE_COLOR.Pending}88`);
    }
    c.strokeStyle = isLab ? STATE_COLOR[lab] : "#64748b";
    c.lineWidth = isLab ? 5 : 2;
    c.strokeRect(rx + 1, y + 1, roomW - 2, roomH - 2);
    c.fillStyle = "#0f172a";
    c.font = `600 18px ${FONT}`;
    c.fillText(name, rx + 10, y + 28);
  });
  c.fillStyle = "#cbd5e1";
  c.fillRect(x, y + roomH + 6, w, h - roomH - 6);
  c.fillStyle = "#475569";
  c.font = `15px ${FONT}`;
  c.fillText("Corridor", x + 10, y + roomH + 32);
}

export function mapScreenTexture(state: MapScreen): THREE.CanvasTexture {
  return canvasTexture(512, 320, (c) => {
    appChrome(c, 512, 320, "Map · HQ Copenhagen · Floor 2");
    floorPlan(c, 16, 56, 480, 200, state);
    const label = state === "HasAccess" ? "Has access" : state === "Pending" ? "Pending" : "No access";
    c.fillStyle = STATE_COLOR[state];
    c.beginPath();
    c.roundRect(16, 272, 220, 34, 17);
    c.fill();
    c.fillStyle = "#fff";
    c.font = `600 18px ${FONT}`;
    c.fillText(`Lab 2.04 · ${label}`, 30, 295);
  });
}

export function approverScreenTexture(state: ApproverScreen, role: string): THREE.CanvasTexture {
  return canvasTexture(512, 320, (c) => {
    appChrome(c, 512, 320, `Approvals · ${role}`);
    if (state === "idle") {
      c.fillStyle = "#64748b";
      c.font = `18px ${FONT}`;
      c.fillText("Nothing awaiting your decision", 24, 120);
      return;
    }
    c.fillStyle = "#fff";
    c.strokeStyle = "#cbd5e1";
    c.lineWidth = 2;
    c.beginPath();
    c.roundRect(20, 60, 472, 180, 12);
    c.fill();
    c.stroke();
    c.fillStyle = "#0f172a";
    c.font = `600 22px ${FONT}`;
    c.fillText("Anna Larsen → Lab 2.04", 40, 100);
    c.fillStyle = "#475569";
    c.font = `17px ${FONT}`;
    c.fillText("Security level: manager + area owner", 40, 132);
    c.fillText("Window: Mon-Fri 07:00-19:00", 40, 158);
    if (state === "inbox") {
      c.fillStyle = "#0f172a";
      c.beginPath();
      c.roundRect(40, 180, 130, 42, 8);
      c.fill();
      c.fillStyle = "#fff";
      c.font = `600 18px ${FONT}`;
      c.fillText("Approve", 70, 208);
      c.strokeStyle = "#94a3b8";
      c.beginPath();
      c.roundRect(186, 180, 110, 42, 8);
      c.stroke();
      c.fillStyle = "#334155";
      c.fillText("Reject", 214, 208);
    } else {
      c.fillStyle = STATE_COLOR.HasAccess;
      c.font = `600 20px ${FONT}`;
      c.fillText("✓ Approved", 40, 210);
    }
  });
}

export function securityScreenTexture(flash: boolean): THREE.CanvasTexture {
  return canvasTexture(640, 380, (c) => {
    appChrome(c, 640, 380, "Live · HQ Copenhagen · Floor 2");
    floorPlan(c, 16, 56, 400, 220, null);
    // door markers on the corridor wall
    [0, 1, 2].forEach((i) => {
      const x = 16 + (i + 0.5) * (400 / 3);
      const lit = flash && i === 1;
      c.fillStyle = lit ? STATE_COLOR.HasAccess : "#475569";
      c.beginPath();
      c.arc(x, 56 + 220 * 0.62, lit ? 11 : 7, 0, Math.PI * 2);
      c.fill();
      if (lit) {
        c.strokeStyle = `${STATE_COLOR.HasAccess}66`;
        c.lineWidth = 6;
        c.beginPath();
        c.arc(x, 56 + 220 * 0.62, 22, 0, Math.PI * 2);
        c.stroke();
      }
    });
    c.fillStyle = "#0f172a";
    c.font = `600 17px ${FONT}`;
    c.fillText("Recent events", 432, 76);
    const rows = flash ? [["Granted", "Anna Larsen", "Lab 2.04 · In", STATE_COLOR.HasAccess]] : [];
    rows.push(["Granted", "Oliver Kruse", "Lobby · In", "#64748b"], ["Denied", "Visitor", "2.06 Comms", "#64748b"]);
    rows.forEach(([kind, who, where, color], i) => {
      const y = 104 + i * 58;
      c.fillStyle = i === 0 && flash ? `${STATE_COLOR.HasAccess}22` : "#f1f5f9";
      c.fillRect(428, y - 20, 196, 50);
      c.fillStyle = color ?? "#64748b";
      c.font = `600 15px ${FONT}`;
      c.fillText(kind ?? "", 438, y);
      c.fillStyle = "#334155";
      c.font = `14px ${FONT}`;
      c.fillText(`${who ?? ""} · ${where ?? ""}`, 438, y + 20);
    });
  });
}

/** Contactless symbol and brand-free markings on the reader face. */
export function readerFaceTexture(): THREE.CanvasTexture {
  return canvasTexture(128, 224, (c) => {
    const g = c.createLinearGradient(0, 0, 128, 224);
    g.addColorStop(0, "#2b3036");
    g.addColorStop(1, "#16191d");
    c.fillStyle = g;
    c.fillRect(0, 0, 128, 224);
    c.strokeStyle = "#c8ced6";
    c.lineWidth = 5;
    c.lineCap = "round";
    [16, 28, 40].forEach((r) => {
      c.beginPath();
      c.arc(46, 128, r, -Math.PI / 4, Math.PI / 4);
      c.stroke();
    });
    c.fillStyle = "#c8ced6";
    c.beginPath();
    c.arc(40, 128, 5, 0, Math.PI * 2);
    c.fill();
  });
}

export function signTexture(number: string, name: string, restricted: boolean): THREE.CanvasTexture {
  return canvasTexture(256, 128, (c) => {
    c.fillStyle = "#1f2933";
    c.fillRect(0, 0, 256, 128);
    c.fillStyle = "#fff";
    c.font = `700 44px ${FONT}`;
    c.fillText(number, 18, 56);
    c.font = `26px ${FONT}`;
    c.fillText(name, 18, 98);
    if (restricted) {
      c.fillStyle = "#f59e0b";
      c.fillRect(200, 16, 40, 40);
      c.fillStyle = "#1f2933";
      c.font = `700 30px ${FONT}`;
      c.fillText("!", 214, 48);
    }
  });
}

export function woodTexture(): THREE.CanvasTexture {
  const t = canvasTexture(256, 512, (c) => {
    c.fillStyle = "#b08155";
    c.fillRect(0, 0, 256, 512);
    for (let i = 0; i < 90; i++) {
      const x = (i * 37) % 256;
      c.strokeStyle = i % 3 === 0 ? "rgba(90,55,30,0.18)" : "rgba(255,230,200,0.08)";
      c.lineWidth = 1 + (i % 4);
      c.beginPath();
      c.moveTo(x, 0);
      c.bezierCurveTo(x + 8, 170, x - 8, 340, x + 4, 512);
      c.stroke();
    }
  });
  return t;
}

/** 600 mm raised-floor tiles (comms room) or 300 mm vinyl tiles (corridor) as a repeating grid. */
export function tileTexture(base: string, line: string, repeat: number): THREE.CanvasTexture {
  const t = canvasTexture(128, 128, (c) => {
    c.fillStyle = base;
    c.fillRect(0, 0, 128, 128);
    c.strokeStyle = line;
    c.lineWidth = 2;
    c.strokeRect(1, 1, 126, 126);
  });
  t.wrapS = THREE.RepeatWrapping;
  t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(repeat, repeat);
  return t;
}
