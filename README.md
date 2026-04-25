# OBS Beyblade X (OBBX) Dashboard

The goal of this project is to create a cross-platform dashboard to control
[OBS Studio](https://obsproject.com/) for beyblade tournaments, by intergrating
with [Challonge](https://challonge.com/) to display Match Information on scenes.

This can be used for displaying Scenes on a stream to push up matches or to
show bracket information on a screen in a better format for viewers.

Player information, Table Information, Deck Lists and Scores can be controled through the Dashboard to display on Stream.

---
# Release

![GitHub tag (latest by date)](https://img.shields.io/github/v/tag/astorec/obbx) [Latest Release](https://github.com/Astorec/OBBX/releases/latest)

---
## Preconditions

First import the OBS Scene Collection from the OBS Files folder and populate the missing files (Need to sor this later)

Enable OOBSWebsocket Via OBS, either LocalHost or IP address of the OBS System can be used. Password from the OBSWebsocket is required to be used here.

Get an API key from [Challonge Connect](https://connect.challonge.com) for settings, Username is also required.

CSV can be used as well to display Blader Deck Information on stream. 

## TODOs

- [x] Add Settings Page to configure Challonge and OBS WebSocket
- [x] Save Settings to app data folder
- [x] Load Settings on app start
- [x] Connect to OBS WebSocket using saved settings
- [x] Fetch Match Information from Challonge using saved settings
- [x] Add scene selection and match pushing functionality
- [x] Add Table selection and prompt to push match info to selected scene
- [x] Add error handling and user feedback for failed connections
- [ ] Add player preview information on dashboard
- [x] Display each match as a Table and click to push to OBS
- [x] Add Break Scene functionality
- [x] Cache Challonge data locally to reduce API calls
- [x] Update Matches automatically when changed in Challonge
- [ ] Have a manual complete match button to get next match info and mark locally until Challonge is refreshed
- [x] Add CSV Import for Beyblade Deck Lists
- [x] Match CSV to player names form Challonge and display deck info on stream
- [x] Manual Edit of Player Deck Info on Dashboard
- [x] Add Table Configurations
- [x] Set number of Tables (Stations) in use
- [x] Assign Matches to Tables automatically - This is now done through the Challonge API
