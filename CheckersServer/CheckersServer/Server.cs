using System;
using System.Threading;
using System.Net.Sockets;
using Checkers.Messages;
using Google.Protobuf.Reflection;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using CheckersServer;

namespace CheckersServer
{
	public class Server
	{
		public enum ServerMode
		{
			WaitingForPlayers,
			OngoingGame
		}

		private ServerMode Mode { get; set; }
		private Thread ConnectionAcceptingThread { get; set; }
		private TcpListener ConnectionListener { get; set; }
		private MessageDispatcher Dispatcher { get; set; }

		private Thread GameLogicThread { get; set; }
		private CheckersMatch Match { get; set; }

		public Server ()
		{
			// prepare checkers match object
			Match = new CheckersMatch();

			// prepare message dispatcher
			Dispatcher = new MessageDispatcher ();

			// prepare listener for player connections
			ConnectionListener = new TcpListener (System.Net.IPAddress.Any, 1337);
			ConnectionListener.Start ();
			ConnectionAcceptingThread = new Thread (AcceptConnections);
			ConnectionAcceptingThread.Start ();
			Mode = ServerMode.WaitingForPlayers;

			// start game logic thread
			GameLogicThread = new Thread(GetGameMessages);
			GameLogicThread.Start ();
		}

		// Lives in a Thread
		private void AcceptConnections () {
			while (true) {
				Console.WriteLine ("waiting for connection");
				TcpClient client = ConnectionListener.AcceptTcpClient ();
				// remove disconnected players
				Dispatcher.RemoveDisconnectedPlayers();

				// Try to connect Red player first.
				if (!Dispatcher.ConnectPlayer (Side.Red, client)) {
					// If that didn't work, try to connect White player.
					if (!Dispatcher.ConnectPlayer (Side.White, client)) {
						Console.WriteLine("Player connect attempt failed.");
						// If that didn't work either, send a message to the connection saying Join didn't work.
						Dispatcher.SendMessage (client, JoinMessage(Side.Unknown));
					} else {
						Console.WriteLine ("White player connected at " + Dispatcher.WhoIsPlayer (Side.White));
						// Connected as white! notify game client.
						Dispatcher.SendMessage (Side.White, JoinMessage(Side.White));
					}
				} else {
					Console.WriteLine ("Red player connected at " + Dispatcher.WhoIsPlayer (Side.Red));
					// Connected as red! notify game client.
					Dispatcher.SendMessage (Side.Red, JoinMessage(Side.Red));
				}

				// if both players were connected, set server mode to ongoing game
				if (Dispatcher.PlayerConnected (Side.Red) && Dispatcher.PlayerConnected (Side.White)) {
					Mode = ServerMode.OngoingGame;
				} else {
					Mode = ServerMode.WaitingForPlayers;
				}
			}
		}

		// Lives in a Thread
		private void GetGameMessages() {
			while (true) {
				// If we don't have both players, wait briefly then check again
				if (Mode == ServerMode.WaitingForPlayers) {
					Thread.Sleep (250);
					continue;
				}

				// Both players are connected, so check for messages and handle them
				CheckersMessage nextMessage;
				if (Dispatcher.RedPlayerConnection.ReceivedMessages.TryDequeue (out nextMessage)) {
					HandleMessage (nextMessage, Side.Red);
				}
				if (Dispatcher.WhitePlayerConnection.ReceivedMessages.TryDequeue (out nextMessage)) {
					HandleMessage (nextMessage, Side.White);
				}
				Thread.Sleep (100);
			}
		}

		private void HandleMessage(CheckersMessage message, Side sender){

			switch (message.MessageType) {
			case MessageType.Move:
				HandleMoveMessage (message, sender);
				break;
			case MessageType.Resign:
				HandleResignMessage (message, sender);
				break;
			default:
				break;
			}
		}

		private void HandleMoveMessage(CheckersMessage message, Side sender){
			if (sender == Match.currentTurn) {
				
				CheckersMatch.Move theMove = new CheckersMatch.Move(message);
				if (Match.IsValidPartialMove(theMove) &&                                 // valid move?
					Match.Board[theMove.StartLocation].Player == sender) {  // checker belongs to player?
					Console.WriteLine ("Got a VALID move message from the player whose turn it is!");
					Match.ApplyMove (theMove);

					// send messages to players notifying them a move was made.
					Dispatcher.SendMessage(Side.Red, theMove.ToMessage());
					Dispatcher.SendMessage(Side.White, theMove.ToMessage());
				}
			} else {
				// not sender's turn; do nothing. else block to explicitly recognize this
			}
		}

		private void HandleResignMessage(CheckersMessage message, Side sender) {
			// inform players of resignation and end game.
			Dispatcher.SendMessage (Side.Red, new CheckersMessage () {
				ProtocolVersion = 1,
				MessageType = MessageType.GameOutcome,
				GameOutcome = (sender == Side.Red ? GameOutcome.RedResign : GameOutcome.WhiteResign)
			});
		}

		public static CheckersMessage JoinMessage(Side side){
			return new CheckersMessage () {
				ProtocolVersion = 1,
				MessageType = MessageType.Join,
				Side = side
			};
		}

		public static CheckersMessage PlayerConnectedMessage(Side side){
			return new CheckersMessage () {
				ProtocolVersion = 1,
				MessageType = MessageType.PlayerConnected,
				Side = side
			};
		}
	}
}

