using System;
using Checkers.Messages;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;


namespace CheckersServer
{
	public class CheckersMatch
	{
		public class Checker {
			public bool IsCrowned { get; set; }
			public bool IsCaptured { get; set; }
			public Side Player { get; set; }
			public BoardLocation Location { get; set; }

			private static int nextID = 0;
			public int ID { get; private set; }
			public Checker(BoardLocation loc, Side player, bool crowned = false, bool captured = false){
				Location = loc;
				Player = player;
				IsCrowned = false;
				IsCaptured = false;
				ID = nextID;
				nextID++;
			}
			public void BecomeCrowned(){
				IsCrowned = true;
			}
		}

		public class BoardLocation {
			public int Row { get; set; }
			public int Col { get; set; }
			public BoardLocation(int row, int col){
				Row = row;
				Col = col;
			}

			public override bool Equals(object o){
				BoardLocation bl = o as BoardLocation;
				if ((object)bl == null) {
					return false;
				}
				return base.Equals (o) || Row == bl.Row && Col == bl.Col;
			}

			public override int GetHashCode ()
			{
				return Row.GetHashCode () * 17 + Col.GetHashCode ();
			}

			public override string ToString ()
			{
				return string.Format ("[BoardLocation: Row={0}, Col={1}]", Row, Col);
			}
		}

		public class Move {
			public BoardLocation StartLocation { get; set; }
			public BoardLocation EndLocation { get; set; }
			public Move(){
			}
			public Move(CheckersMessage moveMessage){
				StartLocation = new BoardLocation(moveMessage.StartRow, moveMessage.StartCol);
				EndLocation = new BoardLocation(moveMessage.EndRow, moveMessage.EndCol);
			}
			public CheckersMessage ToMessage(){
				return new CheckersMessage () {
					ProtocolVersion = 1,
					MessageType = MessageType.Move,
					StartRow = StartLocation.Row,
					StartCol = StartLocation.Col,
					EndRow = EndLocation.Row,
					EndCol = EndLocation.Col
				};
			}
		}

		public Dictionary<BoardLocation, Checker> Board { get; private set; }
		public Side currentTurn; // whose turn is it?
		public Checker forceSamePiece;


		public CheckersMatch ()
		{
			ResetBoard ();
			forceSamePiece = null;
		}

		private void ResetBoard(){
			Board = new Dictionary<BoardLocation, Checker>();
			BoardLocation position;
			Checker checker;

			// add the red pieces
			for (int num = 1; num <= 12; num++) {
				position = new BoardLocation (GetRow (num), GetCol (num));
				checker = new Checker(position, Side.Red);
				Board.Add(position, checker);
				
			}

			// add the white pieces
			for (int num = 21; num <= 32; num++) {
				position = new BoardLocation (GetRow (num), GetCol (num));
				checker = new Checker(position, Side.White);
				Board.Add(position, checker);
			}

			currentTurn = Side.Red;
		}

		// assume this is only called if the given move is valid (correct player's turn, there's a piece there, etc.)
		public void ApplyMove(Move move){
			// remove any captured piece
			bool jump = false;
			bool turnChange = true;
			BoardLocation capturedLocation = JumpsOver(move);
			if (capturedLocation != null) {
				jump = true;
				Board.Remove (capturedLocation);
			}
			Board [move.EndLocation] = Board[move.StartLocation]; // relocate the checker
			Board.Remove (move.StartLocation); // remove
			Board[move.EndLocation].Location = move.EndLocation; // let the checker know its updated position

			// crowning happens first because a continued jump might be in the opposite direction
			if (CrowningPosition (Board[move.EndLocation].Player, move.EndLocation)) {
				Board [move.EndLocation].BecomeCrowned ();
			}

			// if there's still a jump, don't end the turn. otherwise end the turn.
			if (jump) {
				List<Move> nextMoves = GetValidPartialMoves (move.EndLocation);
				if (nextMoves.Count > 0) {
					turnChange = false; // turn only doesn't change if a piece jumped and still has jumps
					forceSamePiece = Board [move.EndLocation];
				}
			}

			if (turnChange){
				forceSamePiece = null;
				if (currentTurn == Side.Red) {
					currentTurn = Side.White;
				} else {
					currentTurn = Side.Red;
				}
						
			}
		}

		// return a board location if the move jumps over another location, and return -1 otherwise
		public BoardLocation JumpsOver(Move move){
			if (Math.Abs (move.StartLocation.Row - move.EndLocation.Row) > 1) {
				BoardLocation jumpedOver = new BoardLocation (
					row: (move.StartLocation.Row + move.EndLocation.Row) / 2,
					col: (move.StartLocation.Col + move.EndLocation.Col) / 2
				);
				return jumpedOver;
			}
			return null;
		}


		public bool CrowningPosition(Side player, BoardLocation loc){
			// red player's crowning positions are 
			if (player == Side.Red && loc.Row == 0 ||
				player == Side.White && loc.Row == 7) {
				return true;
			}
			return false;
		}


		delegate BoardLocation NeighboringPosition(BoardLocation startLocation);
		// A "valid move" is just the next move in a possible chain of moves
		public List<Move> GetValidPartialMoves(BoardLocation startLocation, bool onlyJumps = false) {
			List<Move> moves = new List<Move> ();

			// if there isn't a checker there, can't add any moves!
			if (!Board.ContainsKey (startLocation))
				return moves;
			


			Move candidate;
			if (!onlyJumps) {
				// get the one-away ending locations
				BoardLocation[] StepsToTry = (new NeighboringPosition[] {
					(loc) => GetUpLeft (loc, jump: false),
					(loc) => GetUpRight (loc, jump: false),
					(loc) => GetDownLeft (loc, jump: false),
					(loc) => GetDownRight (loc, jump: false),
				}).Select (// like map in functional languages
					                            neighboringPosition => neighboringPosition (startLocation)
				                            ).ToArray ();
				// add the valid ones
				foreach (BoardLocation possibleEndingLocation in StepsToTry) {
					candidate = new Move () {
						StartLocation = startLocation,
						EndLocation = possibleEndingLocation
					};
					if (IsValidPartialMove(candidate)){
						moves.Add (candidate);
					}
				}
			}

			// get the one-jump-away ending locations
			BoardLocation[] JumpsToTry = (new NeighboringPosition[] {
				(loc) => GetUpLeft(loc, jump: true),
				(loc) => GetUpRight(loc, jump: true),
				(loc) => GetDownLeft(loc, jump: true),
				(loc) => GetDownRight(loc, jump: true),
			}).Select( // like map in functional languages
				neighboringPosition => neighboringPosition(startLocation)
			).ToArray();

			// add the valid jumps
			foreach (BoardLocation possibleEndingLocation in JumpsToTry) {
				candidate = new Move () {
					StartLocation = startLocation,
					EndLocation = possibleEndingLocation
				};
				if (IsValidPartialMove(candidate)){
					moves.Add (candidate);
				}
			}

			return moves;
		}

		// A partial move is either a full move that can be taken by a checker, or a jump among
		// jumps in a chain of jumps, but that is only a single step.
		public bool IsValidPartialMove(Move move) {
			if (forceSamePiece != null) { // if player must use same piece as last time because they jumped, enforce this
				if (Board [move.StartLocation] != forceSamePiece) {
					return false;
				}
			}

			// there should be a checker where the move starts.
			if (!Board.ContainsKey (move.StartLocation)) {
				Debugging.Print ("Invalid move: board doesn't have " + move.StartLocation.ToString ());
				return false;
			}
			Checker checker = Board [move.StartLocation];

			// if there's already a piece where the move ends, the move is invalid.
			if (Board.ContainsKey (move.EndLocation)) {
				Debugging.Print ("Invalid move: landing on another piece.");
				return false;
			}

			// if starts or ends somewhere nonsensical, move is invalid
			if (!isValidBoardPosition (move.StartLocation) || !isValidBoardPosition (move.EndLocation)) {
				Debugging.Print ("Invalid Move: doesn't correspond to a valid board position.");
				return false;
			}

			bool isJump = (Math.Abs (move.EndLocation.Row - move.StartLocation.Row) > 1);
			if (isJump) {
				BoardLocation capturedLocation = JumpsOver (move);
				if ((object)capturedLocation == null) {
					// jumps must have a captured location that is on the board.
					return false;
				}

				bool capturesPiece = Board.ContainsKey (capturedLocation);
				if (!capturesPiece) {
					// captured location must have a piece if it's a jump move
					return false;
				} else if (Board [capturedLocation].Player ==
					Board [move.StartLocation].Player) {
					// captured location must be of the opposite color
					return false;
				}
			} else if (forceSamePiece != null){
				return false;
			}

			if (checker.IsCrowned) {
				// crowned pieces can move anywhere
				bool result = (move.EndLocation.Equals(GetDownLeft (move.StartLocation, isJump)) ||
					move.EndLocation.Equals(GetDownRight (move.StartLocation, isJump)) ||
					move.EndLocation.Equals(GetUpLeft (move.StartLocation, isJump)) ||
					move.EndLocation.Equals(GetUpRight (move.StartLocation, isJump)));
				return result;
			} else if (checker.Player == Side.Red) {
				// red uncrowned pieces can only move down
				bool result = (move.EndLocation.Equals(GetDownLeft (move.StartLocation, isJump)) ||
					move.EndLocation.Equals(GetDownRight (move.StartLocation, isJump)));
				return result;
			} else {
				// white uncrowned pieces
				bool result = (move.EndLocation.Equals(GetUpLeft (move.StartLocation, isJump)) ||
					move.EndLocation.Equals(GetUpRight (move.StartLocation, isJump)));
				return result;
			}

		}

		// always with the "top" of the board on the red side
		// -1 means "not-a-board-position"
		public static bool isValidBoardPosition(BoardLocation location){
			return isValidBoardPosition (location.Row, location.Col);
		}
		public static bool isValidBoardPosition(int row, int col){
			bool OnBlackSquare = (row % 2) == (col % 2); // same parity
			bool RowInBounds = (row >= 0 && row <= 7);
			bool ColInBounds = (col >= 0 && col <= 7);
			return OnBlackSquare && RowInBounds && ColInBounds;
		}


		// Rows are 0-indexed, even though board locations are 1-indexed
		// bottom left is 0
		private int GetRow(int boardLocation){
			return (7 - (boardLocation - 1) / 4);
		}

		// Columns are also 0-indexed.
		private int GetCol(int boardLocation){
			return 2 * ((boardLocation - 1) % 4) + (GetRow (boardLocation) % 2);
		}

		private BoardLocation GetDownLeft(BoardLocation start, bool jump = false){
			if (!jump){
				return new BoardLocation (start.Row - 1, start.Col - 1);
			}
			return new BoardLocation (start.Row - 2, start.Col - 2);
		}

		private BoardLocation GetDownRight(BoardLocation start, bool jump = false){
			if (!jump){
				return new BoardLocation (start.Row - 1, start.Col + 1);
			}
			return new BoardLocation (start.Row - 2, start.Col + 2);
		}

		private BoardLocation GetUpLeft(BoardLocation start, bool jump = false){
			if (!jump){
				return new BoardLocation (start.Row + 1, start.Col - 1);
			}
			return new BoardLocation (start.Row + 2, start.Col - 2);
		}

		private BoardLocation GetUpRight(BoardLocation start, bool jump = false){
			if (!jump){
				return new BoardLocation (start.Row + 1, start.Col + 1);
			}
			return new BoardLocation (start.Row + 2, start.Col + 2);
		}

		// if winner were determined right now, who would it be?
		public Side ComputeWinner(){
			// check number of pieces left
			List<Checker> redCheckers = new List<Checker>();
			List<Checker> whiteCheckers = new List<Checker> ();
			// get a count of checkers remaining
			foreach (Checker checker in Board.Values) {
				if (checker.Player == Side.Red) {
					redCheckers.Add (checker);
				} else if (checker.Player == Side.White) {
					whiteCheckers.Add (checker);
				} else {
					throw new Exception ("Server had an un-owned checker on its board. Checker info:\n" + checker.ToString());
				}
			}

			// no checkers => you lose
			if (redCheckers.Count == 0) {
				return Side.White;
			} else if (whiteCheckers.Count == 0) {
				return Side.Red;
			}

			// both players have checkers left, so see if someone can't move
			bool redHasNoMoves = true;
			foreach (Checker checker in redCheckers) {
				if (GetValidPartialMoves (checker.Location).Count > 0) {
					redHasNoMoves = false;
				}
			}
			bool whiteHasNoMoves = true;
			foreach (Checker checker in redCheckers) {
				if (GetValidPartialMoves (checker.Location).Count > 0) {
					whiteHasNoMoves = false;
				}
			}
			if (whiteHasNoMoves && redHasNoMoves) {
				return Side.Both; // tie if both players can't move
			} else if (whiteHasNoMoves) {
				return Side.Red;
			} else if (redHasNoMoves) {
				return Side.White;
			}

			// no standard rule says which player wins here.
			return Side.Both;
		}
	}
}

