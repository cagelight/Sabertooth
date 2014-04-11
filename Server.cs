using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Sabertooth.Lexicon;
using Sabertooth.Mandate;
using Sabertooth.HTTP;

namespace Sabertooth {
	internal class Server {
		private bool Stopping = false;
		private TcpListener ClientListener;
		private Thread ClientProcessor;
		private MandateManager Gen;
		StreamWriter AL = File.AppendText (Path.Combine(Environment.CurrentDirectory, "connections.log"));
		public Server() {
			ClientListener = new TcpListener (IPAddress.Any, 80);
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
			try {
				ClientTranslator Concierge = new ClientTranslator (Client);
				//Concierge.SendHTTP (Response.Standard100);
				bool first = true;
				while(true){
					ClientRequest clientInfo = Concierge.ProcessRequest ();
					if (clientInfo == null) {
						break;
					}
					Response R = GenerateResponse (clientInfo);
					if (first) {
						AL.WriteLine(String.Format("{0}: {1}", clientInfo.Address.ToString(), clientInfo.Host + clientInfo.Path));
						AL.Flush();
						first = false;
					}
					switch (clientInfo.Type) {
					case "HEAD":
						Concierge.SendHTTP (R, false);
						break;
					default:
						Concierge.SendHTTP (R);
						break;
					}
				}
				Concierge.Close ();
				Client.Close ();
			} catch (Exception e) {
				Console.WriteLine(e);
			}
		}

		private Response GenerateResponse(ClientRequest CR) {
			if (CR.Host == null) {
				Response BR = new Response (Response.Code.N400, new TextResource("Sabertooth requires that clients have Host fields in their headers."));
				BR.connectionStatus = Instruction.Connection.Close;
				return BR;
			}
			if (!(CR.Type == "GET" || CR.Type == "POST" || CR.Type == "HEAD")) {
				Response BR = new Response (Response.Code.N400, new TextResource ("Sabertooth only supports GET, POST, and HEAD at the moment."));
				return BR;
			}
			string realm;
			Tuple<string, string> auth = CR.Authorization;
			if (!Gen.IsAuthorized(CR, auth, out realm)) {
				Response AUTH = new Response (Response.Code.N401);
				AUTH.AddInstruction (Instruction.Authenticate(realm));
				return AUTH;
			}
			CacheData CD = Gen.GetCacheData (CR);
			if ((CR.LastModified != new DateTime() && CR.LastModified == CD.LastModified) || (CR.LastModified == new DateTime() && CD.ETag != String.Empty && CR.ETag == CD.ETag)) {
				Response NM = new Response (Response.Code.N304);
				NM.MaxAge = CD.MaxAge;
				return NM;
			}
			Response Res;
			try {
				ClientBody CB = CR.ReadBody();
				ClientReturn GenRes;
				if (CB == null) {
					GenRes = Gen.Get(CR);
				} else {
					GenRes = Gen.Post(CR, CB);
				}
				if (GenRes == null)
					throw new ArgumentNullException("The mandate failed to generate a page, hopefully an error log appears above.");
				if (GenRes.Redirect == null) {
					Res = new Response(Response.Code.N200);
				} else {
					Res = new Response(Response.Code.N307);
					Res.AddInstruction(new Instruction("Location", GenRes.Redirect));
				}
				foreach(SetCookie S in GenRes.SetCookies) {
					Res.AddInstruction(Instruction.SetCookie(S));
				}
				if (CD.LastModified != new DateTime()) {
					Res.AddInstruction(Instruction.LastModified(CD.LastModified));
				}
				if (CD.ETag != String.Empty) {
					Res.AddInstruction(Instruction.ETag(CD.ETag));
				}
				Res.SetBody (GenRes.Body);
				Res.MaxAge = GenRes.MaxAge;
			} catch (Exception e) {
				Console.WriteLine (e);
				Response ISE = new Response (Response.Code.N500, new TextResource ("If you are reading this text, the Sabertooth mandate responsible for this request has encountered an error: \n" + e));
				ISE.connectionStatus = Instruction.Connection.Close;
				return ISE;
			}
			return Res;
		}

		public static string GetPath(string URLPath) {
			return Path.Combine (Environment.CurrentDirectory, "Assets", URLPath);
		}
	}
}

