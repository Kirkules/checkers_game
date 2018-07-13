using UnityEngine;
using System.Collections;
using Checkers.Messages;
using UnityEngine.EventSystems;
using System.Diagnostics;

public class Checker : MonoBehaviour
{

	public bool IsCrowned { get; set; }
	public bool IsCaptured { get; set; }
	public Side Player { get; set; }
	public GameBoard.BoardLocation Location { get; set; }
	private static int nextID = 0;
	public int ID;
	public bool selected;

	// Pointer to le board
	public GameBoard Board { get; set; }

	// Piece Outline stuff
	public GameObject OutlinePrefab;
	private GameObject Outline;

	// sprite stuff
	public SpriteRenderer spriteRenderer;
	public Sprite CheckerSprite;
	public Sprite CrownedCheckerSprite;

	public void Start () {
		IsCrowned = false;
		IsCaptured = false;
		ID = nextID;
		nextID++;
		selected = false;

		// setup sprite
		spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
		spriteRenderer.sprite = CheckerSprite;

		// prepare outline
		Outline = GameObject.Instantiate (OutlinePrefab);
		Outline.transform.SetParent (transform);
		Outline.transform.localPosition = Vector3.zero; // center on parent
		Outline.SetActive (false);
	}

	public void Update () {
		if (IsCrowned && spriteRenderer.sprite != CrownedCheckerSprite) {
			spriteRenderer.sprite = CrownedCheckerSprite;
		}
	}

	public void BecomeCrowned(){
		spriteRenderer.sprite = CrownedCheckerSprite;
		IsCrowned = true;
	}

	public void Relocate(GameBoard.BoardLocation loc){
		Location = loc;
		transform.localPosition = new Vector3 (
			GameBoard.BoardXCoord(loc.Col),
			GameBoard.BoardYCoord(loc.Row),
			z: 0);
	}

	public void OnMouseEnter(){
		if (Player == Board.Player && // if this piece belongs to the player
			Board.Player == Board.CurrentTurn) { // if it's also this player's turn
			TurnOnOutline();
		}
	}

	public void OnMouseExit(){
		if (!selected) {
			TurnOffOutline ();
		}
	}

	public void OnMouseDown(){
		
		if (Player == Board.Player && Player == Board.CurrentTurn) {
			// toggle selection
			//selected = !selected;
			if (selected) { // piece was just selected
				// let the board know it's selected
				// Board.SelectPiece (gameObject);
				Board.DeselectPiece();

				// select in unity too
				//Selection.activeGameObject = gameObject;
			} else {
				Board.SelectPiece (gameObject);
				//Board.DeselectPiece ();
			}
		}
	}

	public void TurnOnOutline(){
		if ((object)Outline != null) {
			Outline.SetActive (true);
		}
	}

	public void TurnOffOutline(){
		if ((object)Outline != null) {
			Outline.SetActive (false);
		}
	}

	public void Select(){
		TurnOnOutline ();
		selected = true;
	}

	public void DeSelect(){
		TurnOffOutline ();
		selected = false;
	}
}