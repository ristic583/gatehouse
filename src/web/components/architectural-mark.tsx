/** Decorative architectural illustration, not a map of an actual tenant site. */
export function ArchitecturalMark() {
  const p = (x: number, y: number, z: number) => `${380 + (x - y) * 0.8},${350 + (x + y) * 0.32 - z}`;
  return (
    <svg className="gh-architecture" viewBox="0 0 760 530" fill="none" aria-hidden="true">
      <defs>
        <radialGradient id="arch-glow">
          <stop stopColor="#b2d3b0" stopOpacity=".18" />
          <stop offset="1" stopColor="#b2d3b0" stopOpacity="0" />
        </radialGradient>
      </defs>
      <ellipse cx="390" cy="320" rx="350" ry="215" fill="url(#arch-glow)" />
      {Array.from({ length: 13 }, (_, i) => (
        <g key={i} stroke="#c8debe" strokeOpacity=".08">
          <path d={`M ${p(-340 + i * 55, -280, 0)} L ${p(-340 + i * 55, 280, 0)}`} />
          <path d={`M ${p(-330, -280 + i * 48, 0)} L ${p(330, -280 + i * 48, 0)}`} />
        </g>
      ))}
      {[0, 1, 2, 3].map((f) => (
        <g key={f} className="gh-arch-floor" style={{ animationDelay: `${f * 150}ms` }}>
          <polygon
            points={[p(-190, -125, f * 65), p(190, -125, f * 65), p(190, 125, f * 65), p(-190, 125, f * 65)].join(" ")}
            fill={f === 2 ? "#8fae81" : "#173c35"}
            fillOpacity={f === 2 ? 0.42 : 0.9}
            stroke="#b2cc9c"
            strokeOpacity=".65"
          />
          <path
            d={[
              `M ${p(-190, 125, f * 65)} L ${p(-190, 125, f * 65 - 13)} L ${p(190, 125, f * 65 - 13)}`,
              `L ${p(190, -125, f * 65 - 13)} L ${p(190, -125, f * 65)}`,
              `M ${p(190, 125, f * 65)} L ${p(190, 125, f * 65 - 13)}`,
            ].join(" ")}
            stroke="#a1b28b"
            strokeOpacity=".45"
          />
          {[-70, 65].map((x) => (
            <path
              key={x}
              d={`M ${p(x, -125, f * 65)} L ${p(x, 125, f * 65)} M ${p(-190, 0, f * 65)} L ${p(190, 0, f * 65)}`}
              stroke="#bed4aa"
              strokeOpacity=".35"
            />
          ))}
          {Array.from({ length: 9 }, (_, i) => (
            <path
              key={i}
              d={`M ${p(-170 + i * 40, 125, f * 65 - 13)} L ${p(-170 + i * 40, 125, f * 65 - 55)}`}
              stroke="#90b5a3"
              strokeOpacity=".24"
            />
          ))}
        </g>
      ))}
      <circle cx="496" cy="224" r="6" fill="#d7edb9" />
      <circle cx="496" cy="224" r="13" stroke="#d7edb9" strokeOpacity=".4" />
      <path d="M 510 224 H 620 V 184" stroke="#c7dcac" strokeOpacity=".65" />
      <rect x="543" y="147" width="153" height="38" rx="9" fill="#23463c" stroke="#4e6b55" />
      <text x="561" y="171" fill="#e3edce" fontSize="12" fontFamily="sans-serif">
        ✓ Access, connected.
      </text>
    </svg>
  );
}
