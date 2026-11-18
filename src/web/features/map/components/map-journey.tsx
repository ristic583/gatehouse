"use client";

import { ArrowRight, ArrowUpRight, Box, Check, ChevronRight, Eye, Users } from "lucide-react";
import Link from "next/link";
import { ACCESS_STATE_META, ACCESS_STATES } from "../access-state";

/** Access-state legend and the scene mode note. */
export function MapLegend({ spatial, interiors }: { spatial: boolean; interiors: boolean }) {
  return (
    <div className="gh-model-status">
      <div className="gh-legend">
        {ACCESS_STATES.map((s) => {
          const m = ACCESS_STATE_META[s];
          return (
            <span key={s}>
              <m.icon style={{ color: `var(${m.colorVar})` }} />
              {m.label}
            </span>
          );
        })}
      </div>
      {spatial && (
        <span>
          <Eye size={12} /> {interiors ? "Illustrative interiors enabled" : "Structural view"}
        </span>
      )}
    </div>
  );
}

/** "How access works" strip below the map. */
export function MapJourney({ requestsHref }: { requestsHref: string }) {
  return (
    <>
      <div className="gh-section-heading">
        <div>
          <p className="gh-eyebrow">ACCESS GOVERNANCE</p>
          <h2>Every boundary. Accounted for.</h2>
        </div>
        <Link href={requestsHref}>
          Track my requests <ChevronRight size={12} />
        </Link>
      </div>
      <div className="gh-journey">
        {[
          { title: "Identify the space", text: "Select the exact area. Understand its security policy.", icon: Box },
          { title: "Define your purpose", text: "Specify a bounded access window and justification.", icon: ArrowUpRight },
          { title: "Independent review", text: "The designated manager and owner decide in sequence.", icon: Users },
          { title: "System confirmation", text: "Approval is followed by confirmation from the access system.", icon: Check },
        ].map(({ title, text, icon: Icon }, i) => (
          <div key={title}>
            <header>
              <Icon size={19} strokeWidth={1.4} />
              <span>
                0{i + 1}
                <ArrowRight size={10} className="inline ml-2" />
              </span>
            </header>
            <strong>{title}</strong>
            <p>{text}</p>
          </div>
        ))}
      </div>
    </>
  );
}
