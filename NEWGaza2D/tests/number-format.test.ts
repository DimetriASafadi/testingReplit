import test from 'node:test';
import assert from 'node:assert/strict';
import { formatNumber, formatCompactNumber } from '../src/number-format';
import { pageControls } from '../src/ui-layout';

test('balances, prices and counters use Western digits without changing integer amounts', () => {
  assert.equal(formatNumber(0), '0');
  assert.equal(formatNumber(1234567), '1,234,567');
  assert.equal(formatNumber(59.9), '59');
});
test('compact income retains Arabic units but always uses Western digits', () => {
  for (const value of [0, 1500, 15000, 2500000]) {
    const text = formatCompactNumber(value);
    assert.match(text, /[0-9]/);
    assert.doesNotMatch(text, /[٠-٩۰-۹]/);
  }
  assert.match(formatCompactNumber(1500), /ألف/);
});
test('catalog pagination uses Western page and option counts', () => {
  const html = pageControls(0, 12, 'building-page', 1000);
  assert.match(html, /1 \/ 12/);
  assert.match(html, /1,000 خيارًا/);
  assert.doesNotMatch(html, /[٠-٩۰-۹]/);
});
