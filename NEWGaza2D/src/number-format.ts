// Western digits everywhere, while keeping compact amount labels in Arabic.
const integers = new Intl.NumberFormat('ar-EG-u-nu-latn', { maximumFractionDigits: 0 });
const compact = new Intl.NumberFormat('ar-EG-u-nu-latn', { notation: 'compact', maximumFractionDigits: 1 });
export const formatNumber = (value: number) => integers.format(Math.floor(value));
export const formatCompactNumber = (value: number) => compact.format(value);
