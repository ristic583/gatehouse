"use client";
import { interiorKind, type RoomGeometry } from "./geometry";

const metal = "#52616c";
function Box({
  position,
  size,
  color,
  glow,
}: {
  position: [number, number, number];
  size: [number, number, number];
  color: string;
  glow?: boolean;
}) {
  return (
    <mesh position={position} castShadow receiveShadow>
      <boxGeometry args={size} />
      <meshStandardMaterial
        color={color}
        roughness={0.65}
        metalness={0.3}
        emissive={glow ? color : "#000"}
        emissiveIntensity={glow ? 0.7 : 0}
      />
    </mesh>
  );
}
function Chair({ x, z, rotation = 0 }: { x: number; z: number; rotation?: number }) {
  return (
    <group position={[x, 0, z]} rotation={[0, rotation, 0]}>
      <Box position={[0, 0.22, 0]} size={[0.27, 0.07, 0.27]} color="#647381" />
      <Box position={[0, 0.39, 0.12]} size={[0.28, 0.31, 0.045]} color="#647381" />
      <Box position={[0, 0.1, 0]} size={[0.05, 0.2, 0.05]} color={metal} />
    </group>
  );
}
/** Interior props are illustrative and can be disabled; they do not assert real furniture or security hardware. */
export function RoomInterior({ geometry, name }: { geometry: RoomGeometry; name: string }) {
  const kind = interiorKind(name);
  const cells = geometry.furniture;
  if (kind === "meeting") {
    const p = cells[Math.floor(cells.length / 2)];
    if (!p) return null;
    return (
      <group position={[p.x, 0.2, p.z]}>
        <Box position={[0, 0.44, 0]} size={[0.85, 0.065, 0.58]} color="#68797e" />
        <Box position={[0, 0.22, 0]} size={[0.12, 0.44, 0.12]} color={metal} />
        {[-0.25, 0.25].map((x) => (
          <group key={x}>
            <Chair x={x} z={-0.48} />
            <Chair x={x} z={0.48} rotation={Math.PI} />
          </group>
        ))}
        <Box position={[0, 0.49, 0]} size={[0.3, 0.035, 0.19]} color="#151e29" />
      </group>
    );
  }
  return (
    <group>
      {cells
        .filter((_, i) => kind !== "lounge" || i % 2 === 0)
        .map((p, i) => (
          <group key={`${p.x}-${p.z}`} position={[p.x, 0.2, p.z]}>
            {kind === "racks" ? (
              <>
                <Box position={[0, 0.55, 0]} size={[0.52, 1.1, 0.56]} color="#18232d" />
                {[0, 1, 2, 3, 4, 5].map((j) => (
                  <group key={j}>
                    <Box position={[0, 0.15 + j * 0.15, 0.29]} size={[0.43, 0.09, 0.025]} color="#364754" />
                    <Box position={[0.16, 0.15 + j * 0.15, 0.308]} size={[0.055, 0.02, 0.01]} color={j % 2 ? "#b5d969" : "#61d5cf"} glow />
                  </group>
                ))}
              </>
            ) : kind === "storage" ? (
              <>
                <Box position={[0, 0.5, 0]} size={[0.8, 1, 0.38]} color="#4a5864" />
                {[0.2, 0.5, 0.8].map((y) => (
                  <Box key={y} position={[0, y, 0.2]} size={[0.73, 0.025, 0.03]} color="#90a19e" />
                ))}
              </>
            ) : kind === "lounge" ? (
              <>
                <Box position={[0, 0.19, 0]} size={[0.74, 0.32, 0.4]} color="#596e68" />
                <Box position={[0, 0.39, 0.18]} size={[0.74, 0.3, 0.07]} color="#6c8075" />
                <Box position={[0.1, 0.17, -0.43]} size={[0.46, 0.06, 0.27]} color="#9bafa7" />
                {i === 0 && (
                  <mesh position={[0.48, 0.35, 0]}>
                    <cylinderGeometry args={[0.15, 0.12, 0.7, 10]} />
                    <meshStandardMaterial color="#79945b" />
                  </mesh>
                )}
              </>
            ) : (
              <>
                <Box position={[0, 0.45, 0]} size={[0.83, 0.065, 0.5]} color="#9ba6a3" />
                {[-0.33, 0.33].map((x) => (
                  <Box key={x} position={[x, 0.22, 0]} size={[0.035, 0.45, 0.4]} color={metal} />
                ))}
                <Box position={[0, 0.67, 0.13]} size={[0.36, 0.24, 0.035]} color="#183038" />
                <Box position={[0, 0.67, 0.107]} size={[0.31, 0.19, 0.008]} color="#609693" glow />
                <Box position={[0, 0.5, -0.08]} size={[0.29, 0.02, 0.12]} color="#2b363e" />
                <Chair x={0} z={-0.48} />
              </>
            )}
          </group>
        ))}
    </group>
  );
}
