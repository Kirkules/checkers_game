using System;
using Checkers.Messages;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;


public class GameBoard : MonoBehaviour
{
	// child game objects
	public Dictionary<BoardLocation, GameObject> Board { get; private set; }
	public Dictionary<BoardLocation, GameObject> MoveLocations { get; private set; }

	// General Game Info
	public Side Player; // who is this player in the game?
	public Side CurrentTurn; // whose turn is it?
	public GameObject SelectedChecker;
	public Connection ServerConnection;
	public Canvas WaitingScreen;
	private CheckersMessage receivedMessage;
	public GameObject forceSamePiece;
	public bool OpponentConnected;

	// prefabs
	public GameObject RedCheckerPrefab;
	public GameObject WhiteCheckerPrefab;
	public GameObject MoveLocationPrefab;

	// constants
	private static float CheckerSize;

	public void Connect(string host, int port){
		// connect to the server
		ServerConnection = new Connection(new TcpClient(host, port));
	}

	public void Start(){
		gameObject.name = "Main Board";
		gameObject.GetComponentInChildren<Canvas> ().worldCamera = GameObject.Find ("Main Camera").GetComponent<Camera>();
		gameObject.GetComponentInChildren<Canvas> ().sortingLayerName = "UI";
		CheckerSize = GetComponent<SpriteRenderer>().bounds.extents.x/(4f*transform.localScale.x);
		ServerConnection = null;
		ResetBoard ();
		Player = Side.Unknown;
		OpponentConnected = false;
		forceSamePiece = null;

	}

	public void OnDestroy(){
		if (ServerConnection != null) {
			ServerConnection.Shutdown ();
		}
	}


	public void Update(){
		// if we don't know who the current player is (server hasn't said so yet), turn off board
		// and turn on "connecting...." screen thing
		if (Player == Side.Unknown && !WaitingScreen.enabled) {
			WaitingScreen.enabled = true;
		} else if (Player != Side.Unknown && WaitingScreen.enabled) {
			WaitingScreen.enabled = false;
		}

		// check if a message was received from the server
		if (ServerConnection != null) {
			if (ServerConnection.ReceivedMessages.TryDequeue (out receivedMessage)) {
				HandleServerMessage (receivedMessage);
			}
		}
	}


	private void HandleServerMessage(CheckersMessage message) {
		if (message.ProtocolVersion != 1) {
			// don't know how to handle other versions!
			return;
		}
		switch (message.MessageType) {
		case MessageType.Join:
			// set player identity
			Player = message.Side;
			break;
		case MessageType.Move:
			// server says a move was done, so represent that in the game
			ApplyMove(new Move(message));
			break;
		case MessageType.PlayerConnected:
			if (message.Side != Player) {
				Debugging.Print ("opponent connected.");
				OpponentConnected = true;
			}
			break;
		default:
			break;
		}
	}



	private void ResetBoard(){
		Board = new Dictionary<BoardLocation, GameObject>();
		MoveLocations = new Dictionary<BoardLocation, GameObject> ();
		SelectedChecker = null;
		CurrentTurn = Side.Red;


		BoardLocation position;
		GameObject checker;

		// add the red pieces
		for (int num = 1; num <= 12; num++) {
			position = new BoardLocation (GetRow (num), GetCol (num));
			checker = GameObject.Instantiate (RedCheckerPrefab);
			checker.GetComponent<Checker> ().Board = this;
			checker.GetComponent<Checker> ().Player = Side.Red;
			checker.transform.SetParent (transform);
			checker.GetComponent<Checker> ().Relocate (position);
			Board.Add(position, checker);

		}

		// add the white pieces
		for (int num = 21; num <= 32; num++) {
			position = new BoardLocation (GetRow (num), GetCol (num));
			checker = GameObject.Instantiate (WhiteCheckerPrefab);
			checker.GetComponent<Checker> ().Board = this;
			checker.GetComponent<Checker> ().Player = Side.White;
			checker.transform.SetParent (transform);
			checker.GetComponent<Checker> ().Relocate (position);
			Board.Add(position, checker);
		}


		// add the move locations
		GameObject locationMarker;
		for (int loc = 1; loc <= 32; loc++) {
			position = new BoardLocation (GetRow (loc), GetCol (loc));
			locationMarker = GameObject.Instantiate (MoveLocationPrefab);
			locationMarker.transform.SetParent (transform);
			locationMarker.transform.localPosition = new Vector3 (
				BoardXCoord(position.Col),
				BoardYCoord(position.Row),
				z: 0);
			locationMarker.SetActive (false); // not showing available moves to begin with
			locationMarker.GetComponent<MoveLocation>().Location = position;
			locationMarker.GetComponent<MoveLocation> ().Board = this;
			MoveLocations.Add (position, locationMarker);
		}

	}

	public static float BoardXCoord(int col){
		float x = ((float)col - 4f) * CheckerSize + CheckerSize/2f;
		return x;
	}

	public static float BoardYCoord(int row){
		float y = ((float)row - 4f) * CheckerSize + CheckerSize/2f;
		return y;
	}

	public void HideMoveLocations(){
		// TODO: figure out why this doesn't happen when deselecting a piece...
		foreach (BoardLocation location in MoveLocations.Keys){
			MoveLocations[location].SetActive (false);
		}
	}

	public void DeselectPiece(){
		if (forceSamePiece != null) {
			return;
		}
		if (SelectedChecker != null) {
			SelectedChecker.GetComponent<Checker> ().DeSelect ();
		}
		HideMoveLocations ();
	}

	public void SelectPiece(GameObject NewPiece){
		if (forceSamePiece) { // if player is being forced to use the same piece
			HideMoveLocations ();
			ShowMoves (forceSamePiece.GetComponent<Checker> ()); // show its moves
			forceSamePiece.GetComponent<Checker> ().Select (); // make sure it's selected
			return;
		}

		// if checker is not the same one that was selected already
		if (SelectedChecker != NewPiece) {
			DeselectPiece ();
		}

		// select new piece 
		SelectedChecker = NewPiece;
		SelectedChecker.GetComponent<Checker> ().Select ();

		// show moves for the selected piece 
		ShowMoves(NewPiece.GetComponent<Checker>());
	}

	public void ShowMoves(Checker checker){
		// hide moves already being shown
		HideMoveLocations();

		// get available moves for the checker and show them.
		List<Move> availableMoves = GetValidPartialMoves(checker.Location);
		foreach (Move move in availableMoves) {
			// show the marker at the end location of this move
			MoveLocations[move.EndLocation].SetActive(true);
		}
	}


	// assume this is only called if the given move is valid (correct player's turn, there's a piece there, etc.)
	public void ApplyMove(Move move){
		bool jump = false;
		bool turnChange = true;

		// remove any captured piece
		BoardLocation capturedLocation = JumpsOver(move);
		if (capturedLocation != null) {
			jump = true;
			GameObject capturedPiece = Board [capturedLocation];
			Board.Remove (capturedLocation);
			Destroy (capturedPiece);
		}
		Board [move.EndLocation] = Board[move.StartLocation];
		Board.Remove (move.StartLocation);
		Board[move.EndLocation].GetComponent<Checker>().Relocate(move.EndLocation);

		// crowning happens first because a continued jump might be in the opposite direction
		if (CrowningPosition (Board[move.EndLocation].GetComponent<Checker>().Player, move.EndLocation)) {
			Debugging.Print("trying to crown the piece at " + move.EndLocation.ToString() + " for player " + 
				Board[move.EndLocation].GetComponent<Checker>().Player.ToString());
			Board [move.EndLocation].GetComponent<Checker> ().BecomeCrowned ();
		}



		if (jump) {
			// if another jump follows the jump, don't end the turn. otherwise end the turn.
			List<Move> nextJumpMoves = GetValidPartialMoves (move.EndLocation);
			if (nextJumpMoves.Count > 0) {
				Debugging.Print ("next jump moves are:");
				foreach (Move nextJumpMove in nextJumpMoves){
					Debugging.Print(nextJumpMove.ToString());
				}
				turnChange = false; // turn only doesn't change if a piece jumped and still has jumps
				if (CurrentTurn == Player) {
					forceSamePiece = Board [move.EndLocation]; // same player must use this piece next
					SelectPiece (forceSamePiece);
				}
			}
		}

		if (turnChange){
			forceSamePiece = null;
			DeselectPiece ();
			Debugging.Print ("changing turns");
			if (CurrentTurn == Side.Red) {
				CurrentTurn = Side.White;
			} else {
				CurrentTurn = Side.Red;
			}

		}
	}

	public bool CrowningPosition(Side player, BoardLocation loc){
		// red player's crowning positions are 
		if (player == Side.Red && loc.Row == 0 ||
			player == Side.White && loc.Row == 7) {
			return true;
		}
		return false;
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


	public bool IsValidJump(Move move){
	}

	// A partial move is either a full move that can be taken by a checker, or a jump among
	// jumps in a chain of jumps, but that is only a single step.
	public bool IsValidPartialMove(Move move) {
		// there should be a checker where the move starts.
		if (!Board.ContainsKey (move.StartLocation)) {
			return false;
		}
		Checker checker = Board [move.StartLocation].GetComponent<Checker>();

		// if there's already a piece where the move ends, the move is invalid.
		if (Board.ContainsKey (move.EndLocation)) {
			return false;
		}

		// if starts or ends somewhere nonsensical, move is invalid
		if (!isValidBoardPosition (move.StartLocation) || !isValidBoardPosition (move.EndLocation)) {
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
			} else if (Board [capturedLocation].GetComponent<Checker> ().Player ==
			           Board [move.StartLocation].GetComponent<Checker> ().Player) {
				// captured location must be of the opposite color
				return false;
			}
		} else if (forceSamePiece != null) {
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
	private static int GetRow(int boardLocation){
		return (7 - (boardLocation - 1) / 4);
	}

	// Columns are also 0-indexed.
	private static int GetCol(int boardLocation){
		return 2 * ((boardLocation - 1) % 4) + (GetRow (boardLocation) % 2);
	}

	private static BoardLocation GetDownLeft(BoardLocation start, bool jump = false){
		if (!jump){
			return new BoardLocation (start.Row - 1, start.Col - 1);
		}
		return new BoardLocation (start.Row - 2, start.Col - 2);
	}

	private static BoardLocation GetDownRight(BoardLocation start, bool jump = false){
		if (!jump){
			return new BoardLocation (start.Row - 1, start.Col + 1);
		}
		return new BoardLocation (start.Row - 2, start.Col + 2);
	}

	private static BoardLocation GetUpLeft(BoardLocation start, bool jump = false){
		if (!jump){
			return new BoardLocation (start.Row + 1, start.Col - 1);
		}
		return new BoardLocation (start.Row + 2, start.Col - 2);
	}

	private static BoardLocation GetUpRight(BoardLocation start, bool jump = false){
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
		foreach (GameObject checkerSprite in Board.Values) {
			Checker checker = checkerSprite.GetComponent<Checker> ();
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


	// used as dictionary key, so need equality and hashing stuff
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
		public Side Owner;
		public BoardLocation StartLocation { get; set; }
		public BoardLocation EndLocation { get; set; }
		public Move(){
		}
		public Move(CheckersMessage moveMessage){
			Owner = moveMessage.Side;
			StartLocation = new BoardLocation(moveMessage.StartRow, moveMessage.StartCol);
			EndLocation = new BoardLocation(moveMessage.EndRow, moveMessage.EndCol);
		}
		public CheckersMessage ToMessage(){
			return new CheckersMessage () {
				ProtocolVersion = 1, 				// field 1
				MessageType = MessageType.Move,		// field 2
				Side = Owner,						// field 3
				StartRow = StartLocation.Row,		// field 4
				StartCol = StartLocation.Col,		// field 5
				EndRow = EndLocation.Row,			// field 6
				EndCol = EndLocation.Col,			// field 7
				GameOutcome = GameOutcome.Unknown	// field 8
			};
		}
		public override string ToString ()
		{
			return "Move from (" + StartLocation.Row + ", " + StartLocation.Col + ") to (" +
			EndLocation.Row + ", " + EndLocation.Col + ").";
		}
	}
}