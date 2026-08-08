declare const __CLIENT_VERSION__: string;

export const CLIENT_VERSION = __CLIENT_VERSION__;

const CANONICAL_SEMVER = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?$/;

interface ParsedVersion {
  core: [number, number, number];
  prerelease: string[] | null;
}

export function compareProductVersions(left: string, right: string): number | null {
  const leftVersion = parseProductVersion(left);
  const rightVersion = parseProductVersion(right);
  if (!leftVersion || !rightVersion) return null;

  for (let index = 0; index < leftVersion.core.length; index++) {
    const difference = leftVersion.core[index]! - rightVersion.core[index]!;
    if (difference !== 0) return difference < 0 ? -1 : 1;
  }

  if (leftVersion.prerelease === null) return rightVersion.prerelease === null ? 0 : 1;
  if (rightVersion.prerelease === null) return -1;

  const length = Math.max(leftVersion.prerelease.length, rightVersion.prerelease.length);
  for (let index = 0; index < length; index++) {
    const leftPart = leftVersion.prerelease[index];
    const rightPart = rightVersion.prerelease[index];
    if (leftPart === undefined) return -1;
    if (rightPart === undefined) return 1;
    if (leftPart === rightPart) continue;

    const leftNumeric = /^\d+$/.test(leftPart);
    const rightNumeric = /^\d+$/.test(rightPart);
    if (leftNumeric && rightNumeric) return Number(leftPart) < Number(rightPart) ? -1 : 1;
    if (leftNumeric !== rightNumeric) return leftNumeric ? -1 : 1;
    return leftPart < rightPart ? -1 : 1;
  }

  return 0;
}

function parseProductVersion(value: string): ParsedVersion | null {
  const match = CANONICAL_SEMVER.exec(value);
  if (!match) return null;
  return {
    core: [Number(match[1]), Number(match[2]), Number(match[3])],
    prerelease: match[4] ? match[4].split(".") : null
  };
}
