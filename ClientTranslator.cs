using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Sabertooth.Lexicon;

namespace Sabertooth {
	public class ClientTranslator {
		TcpClient Client;
		BufferedStream Communications;
		BinaryWriter CommOut;
		public ClientTranslator(TcpClient client) {
			Client = client;
			Communications = new BufferedStream(Client.GetStream ());
			CommOut = new BinaryWriter (Communications, Encoding.UTF8);
		}

		public void Close() {
			CommOut.Flush (); CommOut = null;
			Communications.Flush (); Communications = null;
		}

		public ClientRequest ProcessRequest() {
			try {
				ClientRequest clientRequest = ProcessHeader();
				clientRequest.IP = ((IPEndPoint)Client.Client.RemoteEndPoint).Address;
				return clientRequest;
			} catch (Exception e) {
				Console.WriteLine ("Failed to process client request: " + e);
				return null;
			}
		}

		private ClientRequest ProcessHeader() {
			ClientRequest CR = null;
			List<string> headerList = new List<string> ();
			string line = String.Empty;
			int cb;
			while (true) {
				byte fail = 0x00;
				while(fail < 0x0A) {
					cb = Communications.ReadByte();
					if(CR == null) {
						CR = new ClientRequest ();
					}
					if(cb == '\n')
						break;
					if(cb == '\r')
						continue;
					if(cb == -1) {
						++fail;
						continue;
					}
					line += Convert.ToChar (cb);
				}
				if(line == String.Empty) {
					break;
				} else {
					Console.WriteLine(line);
					headerList.Add(String.Copy(line));
					line = String.Empty;
				}
			}
			string[] header = headerList.ToArray ();
			string[] requestLine = header [0].Split (' ');
			CR.Type = requestLine[0];

			Statement[] URLArgs;
			int argindex = requestLine[1].LastIndexOf ('?');
			if (argindex == -1 || argindex == requestLine[1].Length-1) {
				URLArgs = new Statement[] { };
				CR.Path = requestLine[1];
			} else {
				List<Statement> argCollection = new List<Statement>();
				string[] rawargs = requestLine[1].Substring(argindex+1).Split('&');
				CR.Path = requestLine[1].Substring(0,argindex);
				foreach(string S in rawargs) {
					if (S.Length < 3) {continue;}
					string[] splitargs = S.Split('=');
					if (splitargs.Length != 2) {continue;}
					argCollection.Add(new Statement(splitargs[0], splitargs[1]));
				}
				URLArgs = argCollection.ToArray();
			}
			CR.Arguments = URLArgs;

			int bodylength = 0;
			MIME bodytype = MIME.OctetStream;
			foreach (string h in header) {
				string[] lineInstruction = h.Split(new string[] {": "}, StringSplitOptions.None);
				switch (lineInstruction[0]) {
				case "Content-Length":
					bodylength = Convert.ToInt32(lineInstruction[1]);
					break;
				case "Content-Type":
					bodytype = MIME.FromText(lineInstruction[1]);
					break;
				case "Host":
					CR.Host = lineInstruction [1];
					break;
				default:
					break;
				}
			}
			if(bodylength > 0) {
				byte[] body = new byte[bodylength];
				Communications.Read(body, 0, bodylength);
				CR.Body = new IStreamableContent[] {new GeneratedResource(body, bodytype)};
			}
			return CR;
		}

		public void SendHTTP(HTTPObject H) {
			H.StreamTo (Communications);
		}
	}
}
