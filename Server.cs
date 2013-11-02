using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Sabertooth.Lexicon;
using Sabertooth.Mandate;

namespace Sabertooth {
	internal class Server {
		private bool Stopping = false;
		private TcpListener ClientListener;
		private Thread ClientProcessor;
		private MandateManager Gen;

		public Server() {
			ClientListener = new TcpListener (IPAddress.Any, 8080);
			Gen = new MandateManager();
		}
		public void Start() {
			Gen.Begin ();
			ClientListener.Start ();
			ClientProcessor = new Thread (ClientGateway);
			ClientProcessor.Start ();
		}
		public void Stop()  {
			Stopping = true;
			ClientListener.Stop ();
			Gen.End ();
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
				Response R = GenerateResponse (clientInfo);
				Concierge.SendHTTP (R);
			}
			//Console.WriteLine (String.Format("\n\nConnection from {0} closed.", ((IPEndPoint)Client.Client.RemoteEndPoint).Address));
			Concierge.Close ();
			Client.Close ();
		}

		private Response GenerateResponse(ClientRequest CR) {
			if(CR.Host == null)
				return Response.Standard400;
			if (!(CR.Type == "GET" || CR.Type == "POST" || CR.Type == "HEAD"))
				return Response.Standard501;
			Response Res = new Response (Response.Code.N200);
			Res.AddInstruction (HTTPObject.Instruction.ConnectionKeepAlive);
			try {
				Res.SetBody (Gen.Get(CR));
			} catch (Exception e) {
				Console.WriteLine ("An internal error was encountered in the Mandate assemblies: \n{0}", e);
				return Response.Standard500;
			}
			if (CR.Type == "HEAD")
				return (HEADResponse)Res;
			return Res;
		}

		public static string GetPath(string URLPath) {
			return Path.Combine (Environment.CurrentDirectory, "Assets", URLPath);
		}
	}
}

