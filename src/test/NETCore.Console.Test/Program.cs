﻿using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL;

class Program
{
    const string AllowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    const string WebsocketTestServerUrl = "wss://ws.postman-echo.com/raw";
    //const string WebsocketTestServerUrl = "wss://ws.ifelse.io";
    //const string WebsocketTestServerUrl = "http://ubuntusrv2.my.home:3000/socket.io/?EIO=4&transport=websocket";
    //const string WebsocketTestServerUrl = "172.19.128.84:3000";
    //const string WebsocketTestServerUrl = "localhost:3000";

    static async Task Main()
    {

        var outerCancellationSource = new CancellationTokenSource();

        await StartWebSocketAsyncWithRetry(outerCancellationSource);

        System.Console.WriteLine("Waiting...");
        System.Console.ReadKey();
        outerCancellationSource.Cancel();
    }

    private static async Task StartWebSocketAsyncWithRetry(CancellationTokenSource outerCancellationTokenSource)
    {
        var tcpClient = new TcpClient { LingerState = new LingerOption(true, 0) };


        while (!outerCancellationTokenSource.IsCancellationRequested)
        {
            var innerCancellationSource = new CancellationTokenSource();

            await StartWebSocketAsync(tcpClient, innerCancellationSource);

            while (!innerCancellationSource.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), innerCancellationSource.Token);
            }

            // Wait 5 seconds before trying again
            await Task.Delay(TimeSpan.FromSeconds(5), outerCancellationTokenSource.Token);
        }
    }

    private static async Task StartWebSocketAsync(
        TcpClient tcpClient,
        CancellationTokenSource innerCancellationTokenSource)
    {
        var websocketClient = new MessageWebsocketRx(tcpClient)
        {
            IgnoreServerCertificateErrors = true,
            Headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } },
            TlsProtocolType = SslProtocols.Tls12,
            ExcludeZeroApplicationDataInPong = true,
        };

        Console.WriteLine("Start");

        IObservable<(string message, ConnectionStatus state)> websocketConnectionObservable = 
            websocketClient.WebsocketConnectWithStatusObservable(
               new Uri(WebsocketTestServerUrl), 
               hasClientPing: false,
               clientPingTimeSpan: TimeSpan.FromSeconds(10));

        var disposableConnectionStatus = websocketConnectionObservable
            .Do(tuple =>
            {
                Console.WriteLine(tuple.state.ToString());

                if (tuple.state == ConnectionStatus.Disconnected
                || tuple.state == ConnectionStatus.Aborted
                || tuple.state == ConnectionStatus.ConnectionFailed)
                {
                    innerCancellationTokenSource.Cancel();
                }

                if (tuple.state == ConnectionStatus.MessageReceived 
                    && tuple.message is not null)
                {
                    Console.WriteLine($"Echo: {tuple.message}");
                }
            })
            .Where(tuple => tuple.state == ConnectionStatus.WebsocketConnected)
            .Select(_ => Observable.FromAsync(_ => SendTest1()))
            .Concat()
            .Select(_ => Observable.FromAsync(_ => SendTest2()))
            .Concat()
            .Subscribe(
            _ => { },
            ex =>
            {
                Console.WriteLine($"Connection status error: {ex}.");
                innerCancellationTokenSource.Cancel();
            },
            () =>
            {
                Console.WriteLine($"Connection status completed.");
                innerCancellationTokenSource.Cancel();
            });

        //var disposableWebsocketMessage = websocketConnectionObservable.Subscribe(msg =>
        //    {
        //        Console.WriteLine($"Echo: {msg}");
        //    },
        //    ex =>
        //    {
        //        Console.WriteLine(ex.Message);
        //        innerCancellationTokenSource.Cancel();
        //    },
        //    () =>
        //    {
        //        Console.WriteLine($"Message listener subscription Completed");
        //        innerCancellationTokenSource.Cancel();
        //    });

        await Task.Delay(TimeSpan.FromSeconds(200));

        async Task SendTest1()
        {
            var sender = websocketClient.GetSender();

            await Task.Delay(TimeSpan.FromSeconds(4));

            await sender.SendTextAsync("Test Single Frame 1");

            await Task.Delay(TimeSpan.FromSeconds(5));

            await sender.SendTextAsync("Test Single Frame 2");

            await sender.SendTextAsync("Test Single Frame again");

            await sender.SendTextAsync(TestString(1024, 1024));

            //await Task.Delay(TimeSpan.FromSeconds(2));

            await sender.SendTextAsync(new[] { "Test ", "multiple ", "frames ", "1 ", "2 ", "3 ", "4 ", "5 ", "6 ", "7 ", "8 ", "9 " });

            await sender.SendTextAsync("Start ", FrameTypeKind.FirstOfMultipleFrames);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendTextAsync("Continue... #1 ", FrameTypeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendTextAsync("Continue... #2 ", FrameTypeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(550));
            await sender.SendTextAsync("Continue... #3 ", FrameTypeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendTextAsync("Stop.", FrameTypeKind.LastInMultipleFrames);           

            await Task.Delay(TimeSpan.FromSeconds(20));
        }

        async Task SendTest2()
        {
            var sender = websocketClient.GetSender();

            Console.WriteLine("Sending: Test Single Frame");
            await sender.SendTextAsync("Test Single Frame");

            await sender.SendTextAsync("Test Single Frame again");

            await sender.SendTextAsync(TestString(1024, 1024));

            await sender.SendTextAsync(new[] { "Test ", "multiple ", "frames" });

            await sender.SendTextAsync("Start ", FrameTypeKind.FirstOfMultipleFrames);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendTextAsync("Continue... #1 ", FrameTypeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendTextAsync("Continue... #2 ", FrameTypeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(550));
            await sender.SendTextAsync("Continue... #3 ", FrameTypeKind.Continuation);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await sender.SendTextAsync("Stop.", FrameTypeKind.LastInMultipleFrames);

            await Task.Delay(TimeSpan.FromDays(1));
        }
        //catch (Exception e)
        //{
        //    Console.WriteLine(e);
        //    innerCancellationTokenSource.Cancel();
        //}
        
    }

    private static string TestString(int minlength, int maxlength)
    {

        var rng = new Random();

        return RandomStrings(AllowedChars, minlength, maxlength, rng);
    }

    private static string RandomStrings(
        string allowedChars,
        int minLength,
        int maxLength,
        Random rng)
    {
        var chars = new char[maxLength];
        var setLength = allowedChars.Length;

        var length = rng.Next(minLength, maxLength + 1);

        for (var i = 0; i < length; ++i)
        {
            chars[i] = allowedChars[rng.Next(setLength)];
        }

        return new string(chars, 0, length);
    }
}