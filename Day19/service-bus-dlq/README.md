# Day 19 — Service Bus topics + DLQ

## Infrastructure (pre-existing, not created by this change)

| Resource | Value |
|---|---|
| Namespace | `quotes-day19-bus` (Standard, centralindia, `rg-quotesapi-day17`) |
| Endpoint | `https://quotes-day19-bus.servicebus.windows.net:443/` |
| Topic | `quote-events` |
| Subscriptions | `audit-log`, `search-index` — both `maxDeliveryCount 3`, `lockDuration PT1M` |
| Auth | `DefaultAzureCredential`; no connection string or SAS key anywhere in code/config |

## Components

- **Publisher** — [QuoteEndpointExtensions.cs](../../Day5/Task6-Resilience/QuotesApi/Extensions/QuoteEndpointExtensions.cs) publishes `QuoteCreated` to `quote-events` from `POST /api/quotes`, after the quote is committed to SQL. Implementation: [ServiceBusQuoteEventPublisher.cs](../../Day5/Task6-Resilience/QuotesApi/Services/ServiceBusQuoteEventPublisher.cs).
- **Consumer** — `QuotesApi.AuditWorker`, a standalone Worker Service, processes the `audit-log` subscription with `ServiceBusProcessor`. Run two instances to see competing consumers.
- **Poison trigger** — `tools/PoisonMessageSender`, a standalone console tool that sends one on-demand message tagged `ForcePoison=true`.
- **Republish trigger** — `tools/RepublishMessageSender`, a standalone console tool that sends a brand-new message reusing an already-processed quote's `MessageId`, for exercising the idempotency path on demand (the app itself only ever publishes `QuoteCreated` once, at creation time).

`search-index` is provisioned but has no consumer here — out of scope for this exercise; nothing prevents adding one later using the same worker pattern.

## MessageId

`{EventType}:{Id}` — for quote creation, `QuoteCreated:{quote.Id}`. `quote.Id` is SQL Server's own identity value, assigned once per row, so it's already the natural dedupe key for "this quote was created" — no extra state needed to generate it, unlike a random GUID. The `EventType` prefix keeps the id from colliding with a future event type keyed off the same integer (e.g. a future `QuoteDeleted:42`).

The topic does not have `RequiresDuplicateDetection` enabled, so Service Bus's own dedupe window isn't active. The MessageId is still meaningful as the applicationlevel idempotency key described below; enabling namespace-level duplicate detection later would be a free additional layer using the same id.

## Idempotency

Dedupe state lives in one new table, `AuditLogEntries`, in the same Azure SQL database `QuotesApi` already uses (via `QuotesDbContext`, migration `Day19_AddAuditLogEntries`). It has a **unique index on `MessageId`**. There is no separate "have I seen this id" table — for this handler, writing the audit row *is* the work, so the insert and the dedupe check are the same statement.

Per message, `AuditLogWorker`:
1. Inserts `AuditLogEntries { MessageId, EventType, QuoteId, Payload, ProcessedAtUtc }`.
2. On success → `CompleteMessageAsync`.
3. On a unique-constraint violation (SQL error 2601/2627) → this MessageId was already committed by a prior attempt → skip the work, still `CompleteMessageAsync`.
4. On any other exception (including the deliberate poison path) → `AbandonMessageAsync` immediately, so the message becomes available for redelivery right away rather than waiting out the 1-minute lock.

**Genuine duplicate vs. crash-mid-handler redelivery** collapse into the same branch (3, above) — that's the point of keying dedupe off the DB's own unique constraint rather than a separate "mark as seen" flag:

- *Crash after the insert commits but before `CompleteMessageAsync` reaches Service Bus* — lock expires or the process is killed, message redelivers, the retry's insert hits the unique constraint, is recognized as already-done, and completes. No duplicate row, no lost completion.
- *Crash before the insert* — nothing was committed, so the redelivery just reruns cleanly. Not really a duplicate case, just an ordinary retry.
- *Genuine duplicate* (producer republishes the same MessageId, or Service Bus redelivers after a prior successful complete) — identical outcome: unique constraint catches it, no double effect.

## Triggering the poison path on demand

```
dotnet run --project tools/PoisonMessageSender
```

Sends one message to `quote-events` with `ApplicationProperties["ForcePoison"] = true`. `AuditLogWorker` checks that property before touching the database and abandons the message immediately regardless of payload — so 3 delivery attempts (the `audit-log` subscription's `maxDeliveryCount`) happen quickly, and Service Bus moves the message to `audit-log`'s dead-letter sub-queue. Inspect it with:

```
az servicebus topic subscription show --resource-group rg-quotesapi-day17 \
  --namespace-name quotes-day19-bus --topic-name quote-events \
  --subscription-name audit-log --query countDetails
```

or via Service Bus Explorer in the portal, browsing `audit-log/$DeadLetterQueue`.

## Running the consumer

```
dotnet run --project QuotesApi.AuditWorker
```

Run it twice (two terminals) against the same `audit-log` subscription to see competing consumers — Service Bus distributes messages across whichever instance currently holds each message's lock; nothing in the code coordinates between instances beyond that.

## Constraints honored

- No existing endpoint response shape or user-visible string changed. `POST /api/quotes` still returns the same `Created` payload; the Service Bus publish is a side effect wrapped in try/catch, same as the existing `EventLog` enqueue — a Service Bus outage never fails quote creation.
- The Day 18 `EventQueue`/`EventLogDrainService` Channel pipeline is untouched — this is additive. They currently serve different purposes (in-process, best-effort event log vs. durable, at-least-once cross-process delivery) and I'd leave them separate rather than merging; happy to revisit if you want one drain path instead of two.
- Nothing was deployed to the live App Service.
