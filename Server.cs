using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Sabertooth.Lexicon;

namespace Sabertooth {
	internal class Server {
		private bool Stopping = false;
		private TcpListener ClientListener;
		private Thread ClientProcessor;

		public Server() {
			ClientListener = new TcpListener (IPAddress.Any, 8080);
		}
		public void Start() {
			ClientListener.Start ();
			ClientProcessor = new Thread (ClientGateway);
			ClientProcessor.Start ();
		}
		public void Stop()  {
			Stopping = true;
			ClientListener.Stop ();
		}
		private void ClientGateway () {
			while (!Stopping) {
				try {
					TcpClient newClient = ClientListener.AcceptTcpClient ();
					Thread clientSend = new Thread(() => ClientConcierge(newClient));
					clientSend.Start();
				} catch (Exception e) {
					Console.WriteLine ("A severe error has occured during client processing: {0}", e);
				}
			}
		}
		private void ClientConcierge (TcpClient Client) {
			Console.WriteLine (String.Format("Connection from {0} opened.\n\n", ((IPEndPoint)Client.Client.RemoteEndPoint).Address));
			ClientTranslator Concierge = new ClientTranslator (Client);
			Concierge.SendHTTP (Response.Standard100);
			while(true){
				ClientRequest clientInfo = Concierge.ProcessRequest ();
				if (clientInfo == null) {
					break;
				}
				Concierge.SendHTTP (GenerateResponse (clientInfo));
			}
			Console.WriteLine (String.Format("\n\nConnection from {0} closed.", ((IPEndPoint)Client.Client.RemoteEndPoint).Address));
			Concierge.Close ();
			Client.Close ();
		}

		private Response GenerateResponse(ClientRequest CR) {
			if(CR.Host == null) {
				return Response.Standard400;
			}
			Response OKResponse = new Response (Response.Code.N200);
			OKResponse.AddInstruction (HTTPObject.Instruction.ConnectionKeepAlive);
			OKResponse.SetBody (new TextStreamable (String.Format ("You requested: {0}{1}\nThis request was filled in {2} milliseconds.", CR.Host, CR.Path, CR.RequestTime.Mark.TotalMilliseconds)), MIME.Plaintext);
			return OKResponse;
		}
	}
}

