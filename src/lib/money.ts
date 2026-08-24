export function parseAmountToCents(value: string): number {
  if (!/^[1-9]\d*(\.\d{1,2})?$/.test(value)) throw new Error("金额必须大于 0，且最多保留两位小数");
  const [yuan, fraction = ""] = value.split(".");
  return Number(yuan) * 100 + Number(fraction.padEnd(2, "0"));
}
export function formatCents(cents: number) { return `¥${(cents / 100).toFixed(2)}`; }
