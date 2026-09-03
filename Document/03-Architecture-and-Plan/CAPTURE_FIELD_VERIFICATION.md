# ProMaxx2 Capture Companion — Field Verification

## Environment

- Application: `Promaxxs.App.exe`
- Build: `1.0.0-beta.2`
- Runtime path: `C:\Users\Pond_QA\Desktop\ProMaxx2-1.0.0-beta.2`
- Verification mode: read-only UI Automation inspection

## Verified after login

The Dashboard exposed these enabled navigation controls:

`Dashboard`, `Inventory`, `Person`, `Transaction_Purchase`, `Transaction`, `Utility`, `Settings`, `Reporting`.

## Inventory result

After the user opened Inventory, the page title `คลังสินค้า` and 11 `Promaxx.UI.Controls.HubCardItem` controls were visible. The card controls did not expose AutomationId.

### Required Dev action

Assign unique, stable AutomationIds to each actionable Inventory card. Do not use card text, index, coordinate, or image as the primary selector. Re-run the runtime scanner after the next build and update the registry in `AUTOMATIONID_IMPLEMENTATION_GUIDE.md` and `SELECTOR_CONTRACT.md`.

## Safety

No login credentials, passwords, database values, or business mutations are recorded in this document. No business action was performed during verification.
