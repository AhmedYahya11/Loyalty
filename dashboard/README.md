# Loyalty Dashboard · لوحة تحكم الولاء

Bilingual (AR/EN, RTL-aware) admin dashboard for the Loyalty Platform. Next.js (App Router, TS).
Client-side; talks to the Loyalty API with the API key you enter on the **Connect** page.

## Run

```bash
npm install
npm run dev        # http://localhost:3000  (API expected at http://localhost:8080)
```

## Pages

| Page | Does |
|---|---|
| **Connect** | Set API base URL + key; one-click **bootstrap** the root application (first run) |
| **Applications** | List the visible application tree; create child applications (bilingual names) |
| **Campaigns** | Set loyalty settings (spend-per-point); create/activate/pause purchase-earn campaigns |
| **Members** | Create a member; look up wallet balance + immutable ledger transactions |
| **Events** | Submit a test purchase event; poll request status + awarded points |

Toggle العربية/English in the sidebar — the whole UI flips to RTL and sends `Accept-Language`
so API error messages come back localized too.
