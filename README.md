# Market Assistant - FFXIV Dalamud Plugin

A complete, **100% ToS-compliant** marketplace trading assistant for FFXIV that helps you track market prices, analyze flipping opportunities, and manage your listings with zero automation.

## Features

### 🔍 Price Tracking
- Track items you're selling on the market board
- Monitor undercut alerts when competitors lower prices
- View 90-day price history with trend analysis (Rising/Falling/Stable)
- Identify price patterns and volatility

### 💰 Flipping Analysis
- **Buy Now**: Identify items currently below average price (5%+ discount)
- **Sell Now**: Find items trading above average (premium opportunities)
- **High Volatility**: Spot items with significant price swings
- **Profit Calculator**: All flips show profit potential in gil and percentage
- **Watchlist**: Monitor target buy/sell prices and get alerts

### 🌐 Universalis Monitoring
- Optional background refresh from Universalis API (default every 5 minutes)
- Manual world sync command when you want immediate updates
- Continuous sell-signal evaluation for active flips
- In-game Active Flips panel for actionable sell timing recommendations

### 📊 Profit Management
- Tax-aware profit calculations
- Per-item profit margins tracking
- Bulk price adjustment helper with preview
- Profit statistics and analysis

### 🎯 Completely Manual
All features require player action:
- No automatic price changes
- No unattended market board interaction
- No bot-like behavior
- Player makes all trading decisions

## Installation

### Requirements
- FFXIV with XIVLauncher installed
- Dalamud plugin loader enabled
- .NET 8.0 (for building from source)

### Quick Install
1. Open Dalamud Settings → Experimental
2. Add plugin repo: `https://[your-repo-url]/dalamud`
3. Install "Market Assistant"
4. `/xlplugins` to manage plugins

### Build from Source
```bash
git clone https://github.com/yourname/UndercutterFFXIV.git
cd UndercutterFFXIV
dotnet build -c Release
# Copy bin/Release/UndercutterFFXIV.dll to XIVLauncher/addon/Hooks/dev/plugins/UndercutterFFXIV/
```

## Commands

- `/ma` - Open main Market Assistant window
- `/ma flip` - View flipping opportunities
- `/ma tracker` - See flip statistics and watchlist
- `/ma active` - Open active flip monitor and sell suggestions
- `/ma sync [world]` - Manual Universalis refresh (uses configured world if omitted)
- `/ma config` - Open settings
- `/ma search` - Search items in tracker
- `/ma history` - View 90-day price trends
- `/ma profit` - Show profit analysis
- `/ma adjust` - Bulk price adjustment tool

## Workflow Examples

### Finding a Flip Opportunity
1. `/ma flip` to see all analyzed items
2. Browse tabs: "Buy Now", "Sell Now", "High Volatility"
3. Click "Add to Watchlist" on items that interest you
4. Watch for alerts in Watchlist tab
5. When price is right, manually buy on market board
6. Later, sell at higher price (player chooses when)

### Active Sell Suggestions
1. `/ma active` to open Active Flips
2. Add the item you bought (name/id, buy price, quantity, target sell)
3. Let background sync keep prices current, or run `/ma sync` for immediate updates
4. Review sell signals in the Suggestions column:
	- Target reached
	- Minimum profit met
	- Price trend turning down
	- Held too long with acceptable profit
5. Use "Mark Sold" to record the completed flip when you manually sell in-game

### Monitoring Your Listings
1. `/ma` to open main window
2. Add items you're selling with `/ma search`
3. Plugin alerts when undercut
4. Click prices to copy to clipboard
5. Visit market board and manually adjust

### Bulk Adjusting Prices
1. `/ma adjust` to open adjustment tool
2. Select items to reprice
3. Preview new suggested prices
4. Copy prices to clipboard
5. Go to market board
6. Manually type each new price

## Features in Detail

### Flip Opportunities Analysis
- **All Opportunities**: Sorted by profit potential
- **Buy Now**: Items 5%+ below average price
- **Sell Now**: Items 5%+ above average price
- **High Volatility**: Items with >5% price swings

Each shows:
- Item name and buy/sell prices
- Profit per unit (in gil)
- Profit percentage
- Price volatility/trend
- Action button to add to watchlist

### Flip Tracker Statistics
- Total flips completed in period
- Total profit earned
- Average holding time
- Highest single profit
- Per-item analysis

### Watchlist Management
- Track items with target buy/sell prices
- Get alerts when prices hit targets
- Monitor profit goals
- Remove items as needed

## ToS Compliance

✅ **This plugin is fully compliant** with FFXIV Terms of Service

| Feature | Status | Why? |
|---------|--------|------|
| Price recommendations | ✅ Manual | You decide what to do |
| Price history | ✅ Read-only | Only views prices, no actions |
| Profit calculations | ✅ Info only | No financial obligations |
| Watchlist alerts | ✅ Manual trigger | You manually trade |
| Bulk price preview | ✅ Clipboard only | Player types market board prices |
| Auto market posting | ❌ Rejected | Against ToS |
| Auto price changes | ❌ Rejected | Unattended action |
| Bot-like trading | ❌ Rejected | Violates policies |

## Configuration

Settings available at `/ma config`:
- Undercut amount (1-100 gil)
- Check interval (5-300 seconds)
- Tax percentage (0-20%)
- Alert preferences
- History retention
- And more...

## Data Storage

All data stored locally in:
```
%APPDATA%\XIVLauncher\addon\Hooks\dev\plugins\UndercutterFFXIV\data\
```

- `tracked_items.json` - Your listed items
- `price_history.json` - 90 days of market data
- `alerts.json` - Undercut notifications
- `flip_transactions.json` - Completed flips
- `watchlist.json` - Monitored items

## Architecture

### Core Services
- **MarketTracker**: Manages price data and item tracking
- **FlipAnalyzerService**: Analyzes opportunities
- **FlipTrackerService**: Records and analyzes flips
- **PersistenceService**: Saves/loads data
- **NotificationService**: Handles alerts
- **LoggingService**: Debug logging

### Windows
- **MarketBoardWindow**: Main tracking interface
- **FlipOpportunitiesWindow**: Opportunity analysis
- **FlipTrackerWindow**: Statistics and history
- **BulkAdjustWindow**: Price adjustment helper
- **ConfigWindow**: Settings UI
- Plus supporting analysis windows

## Troubleshooting

### Plugin won't load
- Verify Dalamud version is current
- Check plugin.json is valid
- Review logs at `%APPDATA%\XIVLauncher\addon\Hooks\dev\plugins\UndercutterFFXIV\logs\`

### No data appears
- Add items first with `/ma search`
- Update market prices by viewing items on market board
- Check data files exist in data folder

### Windows don't open
- Make sure XIVLauncher is up to date
- Try `/xlreload` to reload plugins
- Check ImGui rendering isn't disabled

## Development

### Tech Stack
- **Framework**: .NET 8.0-windows
- **UI**: ImGui.NET
- **Game Data**: Lumina
- **Dalamud**: Latest API

### Building
```bash
dotnet build -c Release
```

Output: `bin/Release/UndercutterFFXIV.dll`

### Contributing
Contributions welcome! Areas:
- UI improvements
- Better flip analysis algorithms
- Market data visualization
- Performance optimization

## License

MIT License - See LICENSE file

## Support

For issues, suggestions, or questions:
1. Check logs in `logs/` folder
2. Review `/ma config` settings
3. Try reloading with `/xlreload`
4. File an issue on GitHub

## Disclaimer

This plugin is not affiliated with Square Enix. Use at your own risk. Read the COMPLIANCE documentation for detailed ToS analysis.

---

**Last Updated**: 2024
**Version**: 1.0.0
