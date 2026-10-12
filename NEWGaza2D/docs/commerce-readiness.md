# Deferred payment and ad connections

The local-save game supports a real gem-to-coin exchange and an earned-prize wheel.
Neither external purchases nor rewarded advertising is connected. All such controls
are disabled; `CommerceProvider` and `RewardedAdProvider` are preparation interfaces,
not payment implementations.

Before enabling them:
- Put balances, inventory and order fulfillment on an authenticated authoritative server.
- Create checkout sessions on that server, validate signed provider webhooks, and use
  unique event/order IDs for exactly-once fulfillment. Never grant from a redirect alone.
- Verify ad-provider server callbacks before issuing a bonus spin receipt.
- Use authoritative time and cryptographic random prize selection; local saves and
  client clocks are not cheat-resistant or suitable for purchased currency.
- Review currency, minimum charge, taxes, fees, refunds and store policies. The requested
  5–80 cent offers are displayed exactly but may be below a provider's transaction minimum.
- Test cancellations, retries, chargebacks, interrupted sessions and concurrent devices.

Provisional difficulty: easy earns 70%, medium 45%, hard 25% of catalog building income;
new work durations multiply by 1, 1.15 and 1.3. Repeatable project payouts preserve the
invested principal and scale only profit, avoiding a loss-making progression loop.
Old completed building income retains its earned value until collection, after which
the next batch follows district difficulty. Existing deadlines are not rebalanced.
