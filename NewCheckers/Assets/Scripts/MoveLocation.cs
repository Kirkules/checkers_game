using UnityEngine;
using System.Collections;
using UnityEngine.SocialPlatforms;

public class MoveLocation : MonoBehaviour {

	public GameBoard.BoardLocation Location; // set by the board
	public GameBoard Board;

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
	
	}

	public void OnMouseDown(){
		// send message for move from selected checker to this location
		GameBoard.Move theMove = new GameBoard.Move () {
			Owner = Board.Player,
			StartLocation = Board.SelectedChecker.GetComponent<Checker>().Location,
			EndLocation = Location
		};

		Board.ServerConnection.SendMessage (theMove.ToMessage ());
		Debugging.Print ("Should have just sent a message: " + theMove.ToMessage ());
		Board.DeselectPiece ();
	}
}
