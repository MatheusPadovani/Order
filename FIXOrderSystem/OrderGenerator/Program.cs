using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;

namespace OrderGenerator
{
    public class OrderRequest
    {
        public string? Symbol { get; set; }
        public string? Side { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }

    public class OrderResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

    public class FixApp : MessageCracker, IApplication
    {
        public SessionID? MySessionID;
        public ConcurrentDictionary<string, ExecutionReport> ReceivedReports = new ConcurrentDictionary<string, ExecutionReport>();

        public void FromAdmin(QuickFix.Message message, SessionID sessionID) { }
        public void FromApp(QuickFix.Message message, SessionID sessionID) { Crack(message, sessionID); }
        public void OnCreate(SessionID sessionID) { }
        public void OnLogon(SessionID sessionID) { MySessionID = sessionID; }
        public void OnLogout(SessionID sessionID) { MySessionID = null; }
        public void ToAdmin(QuickFix.Message message, SessionID sessionID) { }
        public void ToApp(QuickFix.Message message, SessionID sessionID) { }

        public void OnMessage(ExecutionReport report, SessionID sessionID)
        {
            if (report.IsSetClOrdID())
            {
                ReceivedReports[report.ClOrdID.getValue()] = report;
            }
        }

        public OrderResponse SendOrder(OrderRequest req)
        {
            if (MySessionID == null)
            {
                return new OrderResponse { Success = false, Message = "Erro: Sem conexão com o acumulador." };
            }

            string clOrdId = Guid.NewGuid().ToString("N");
            char side = req.Side == "Compra" ? Side.BUY : Side.SELL;

            NewOrderSingle order = new NewOrderSingle(
                new ClOrdID(clOrdId),
                new Symbol(req.Symbol ?? ""),
                new Side(side),
                new TransactTime(DateTime.UtcNow),
                new OrdType(OrdType.LIMIT)
            );

            order.Set(new OrderQty(req.Quantity));
            order.Set(new Price(req.Price));

            Session.SendToTarget(order, MySessionID);

            int elapsed = 0;
            ExecutionReport? report = null;

            while (elapsed < 5000)
            {
                if (ReceivedReports.TryGetValue(clOrdId, out report))
                {
                    break;
                }
                Thread.Sleep(100);
                elapsed += 100;
            }

            if (report != null)
            {
                bool success = report.ExecType.getValue() == ExecType.NEW;
                string msg = success ? "Ordem aceita com sucesso!" : "Ordem rejeitada.";
                if (report.IsSetText())
                {
                    msg += " " + report.Text.getValue();
                }
                return new OrderResponse { Success = success, Message = msg };
            }

            return new OrderResponse { Success = false, Message = "Erro: Tempo esgotado." };
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var fixApp = new FixApp();
            var app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            SessionSettings settings = new SessionSettings("generator.cfg");
            IMessageStoreFactory storeFactory = new MemoryStoreFactory();
            ILogFactory logFactory = new ScreenLogFactory(settings);
            SocketInitiator initiator = new SocketInitiator(fixApp, storeFactory, settings, logFactory);
            
            initiator.Start();
            
            app.MapPost("/api/orders", (OrderRequest request) => Results.Ok(fixApp.SendOrder(request)));

            app.Lifetime.ApplicationStopping.Register(() => initiator.Stop());

            app.Run();
        }
    }
}
