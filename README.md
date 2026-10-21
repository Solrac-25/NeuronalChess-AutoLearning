# NeuronalChess

A chess AI written from scratch in C# (no external libraries) that learns by playing against itself. A neural network (768 inputs → 512 → 256 neurons, with a policy head and a value head) picks the moves for both sides, and after every game it trains on what happened, so it gets better with each game it plays. 10% of the moves are random exploration, which teaches it to tell legal moves from illegal ones. Its "brain" is saved to chess_brain.bin and its statistics to chess_stats.json, so progress is kept between sessions. The program shows the board in the console, the evaluation of each position and a report at the end of every game.

# Download and play (no installation needed)

Pre-built versions are available on the Releases page. Download the zip for your system, extract it and run it:

Windows (64-bit): double-click NeuronalChess.exe. If Windows SmartScreen warns you, click More info → Run anyway (the file is not code-signed).

Linux (64-bit): open a terminal in the extracted folder and run 
```
chmod +x NeuronalChess Play.sh && ./NeuronalChess
```
This is a console program, so it must run inside a terminal. If you prefer double-clicking, use Play.sh, which opens a terminal window for you (your file manager may ask whether to run it).
# Build from source

You need the .NET 10 SDK. Then, from the project folder:

``` bash
dotnet run
```
From the menu you can start an AI vs AI game (choosing the seconds between moves), view the statistics or reset its memory. The more games you let it play, the better it gets.
