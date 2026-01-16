# OBS Beyblade X (OBBX) Dashboard

The goal of this project is to create a cross-platform dashboard to control
[OBS Studio](https://obsproject.com/) for beyblade tournaments, by intergrating
with [Challonge](https://challonge.com/) to display Match Information on scenes.

This can be used for displaying Scenes on a stream to push up matches or to
show bracket information on a screen in a better format for viewers.

---

## TODOs

- [x] Add Settings Page to configure Challonge and OBS WebSocket
- [x] Save Settings to app data folder
- [x] Load Settings on app start
- [ ] Connect to OBS WebSocket using saved settings
- [ ] Fetch Match Information from Challonge using saved settings
- [ ] Add scene selection and match pushing functionality
- [ ] Add Table selection and prompt to push match info to selected scene
- [ ] Add error handling and user feedback for failed connections
- [ ] Add player preview information on dashboard
- [ ] Display each match as a Table and click to push to OBS
- [ ] Add Break Scene functionality
- [ ] Cache Challonge data locally to reduce API calls
- [ ] Update Matches automatically when changed in Challonge
- [ ] Have a manual complete match button to get next match info and mark locally until Challonge is refreshed
- [ ] Add CSV Import for Beyblade Deck Lists
- [ ] Match CSV to player names form Challonge and display deck info on stream
- [ ] Manual Edit of Player Deck Info on Dashboard
- [ ] Add Table Configurations
- [ ] Set number of Tables (Stations) in use
- [ ] Assign Matches to Tables automatically
