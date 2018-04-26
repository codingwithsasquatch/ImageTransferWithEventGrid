using Microsoft.Azure.EventGrid;
using Microsoft.Azure.EventGrid.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GridScaleTest
{
    public partial class Form1 : Form
    {

        int clickCount = 0;
        int TotalClicksToPaintPicture = 4;
        int sourceWidth;
        int sourceHeight;
        Bitmap targetImage;
        Bitmap sourceImage;
        Timer pictureRefreshTimer;
        List<Tuple<int, PixelInfo[]>> sentPixelColumns;
        List<Tuple<int, PixelInfo[]>> receivedPixelColumns;
        static string topicEndpoint = "egthroughupeaptopic.eastus2euap-1.eventgrid.azure.net";
        static string topicKey = "IgVbrGPP15cF1SylzKFiFKWr+Dpgt7c4jf/i5AnuM+c=";
        static string sourceImageLocation = @"C:\code\GridScaleTest\GridScaleTest\GridScaleTest\IMG_20180329_151358.jpg";
        private static readonly HttpClient httpClient = new HttpClient();
        private List<EventGridEvent> gridEventsToSend = new List<EventGridEvent>();

        public Form1()
        {
            InitializeComponent();
            this.sourceImage = new Bitmap(sourceImageLocation);
            this.sourceWidth = this.sourceImage.Size.Width;
            this.sourceHeight = this.sourceImage.Size.Height;
            
            this.pictureRefreshTimer = new Timer();
            this.pictureRefreshTimer.Interval = 1000; // In milliseconds
            this.pictureRefreshTimer.Tick += new EventHandler(timer_Tick);
            this.pictureRefreshTimer.Enabled = false;

            this.sentPixelColumns = new List<Tuple<int, PixelInfo[]>>();
            this.receivedPixelColumns = new List<Tuple<int, PixelInfo[]>>();
            this.targetImage = new Bitmap(sourceWidth, sourceHeight);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.targetImage = new Bitmap(sourceWidth, sourceHeight);
            this.clickCount = 0;
            this.pictureRefreshTimer.Enabled = true;
            this.pictureRefreshTimer.Start();

        }

        private void timer_Tick(object sender, EventArgs e)
        {
            this.PaintIncrementalPicture();
            pictureBox1.Image = this.targetImage;
            pictureBox1.Refresh();
        }

        // Divide the picture into N equal parts and draw each part
        //
        private void PaintIncrementalPicture()
        {
            if (this.clickCount < this.TotalClicksToPaintPicture)
            {
                int start = sourceWidth * this.clickCount / this.TotalClicksToPaintPicture;
                int end = sourceWidth * (this.clickCount + 1) / this.TotalClicksToPaintPicture;

                for (int x = start; x < end; x++)
                {
                    for (int y = 0; y < this.sourceHeight; y++)
                    {
                        targetImage.SetPixel(x, y, sourceImage.GetPixel(x, y));
                    }
                }

                this.clickCount++;
            }
            else
            {
                this.pictureRefreshTimer.Stop();
            }
        }

        private async Task SendEvents()
        {
            try
            {
                TopicCredentials topicCredentials = new TopicCredentials(topicKey);
                using (EventGridClient client = new EventGridClient(topicCredentials))
                { 

                    if (gridEventsToSend.Count > 0)
                    {
                        List<EventGridEvent> localEventList = new List<EventGridEvent>();
                        foreach(EventGridEvent localEvent in gridEventsToSend)
                        {
                            if (localEventList.Count == 10)
                            {
                                 client.PublishEventsAsync(topicEndpoint, localEventList);
                                localEventList.Clear();
                            }
                            else
                            {
                                localEventList.Add(localEvent);
                            }
                        }
                        if (localEventList.Count > 0)
                        {
                              client.PublishEventsAsync(topicEndpoint, localEventList);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Exception HTTP Response {ex.Message}");
            }
        }

        private async Task AddEvent(string filename, PictureEvent events)
        {
            var egMessage = new EventGridEvent()
            {
                Subject = filename,
                EventType = "imageeventtype",
                EventTime = DateTime.UtcNow,
                Id = Guid.NewGuid().ToString(),
                DataVersion = "1.0",
                Data = events
            };
            gridEventsToSend.Add(egMessage);
        }

        // Divide the picture and send to Grid
        // 1. Create EventGrid Topic
        //
        private async Task SendPictureAsPixels()
        {
            var newFileName = Guid.NewGuid().ToString() + Path.GetFileName(sourceImageLocation);
            var arraySize = 500;
            for (int x = 0; x < this.sourceWidth; x++)
            {
                var columnList = new PixelInfo[this.sourceHeight];
                var eventsSubArray = new PixelInfo[arraySize];
                var subarrayOffset = 0;
                for (int y = 0; y < this.sourceHeight; y++)
                {
                    eventsSubArray[subarrayOffset] = new PixelInfo(x, y, sourceImage.GetPixel(x, y).ToArgb());
                    if (subarrayOffset == arraySize-1)
                    {
                        AddEvent(newFileName, new PictureEvent(){ x = x, pixels = eventsSubArray});
                        subarrayOffset = 0;
                        eventsSubArray = new PixelInfo[arraySize]; //reset the array
                    }
                    else
                    {
                        subarrayOffset++;
                    }
                }
                if (eventsSubArray.Length > 0)
                {
                    eventsSubArray = eventsSubArray.Where(pixelinfo => !(pixelinfo == null)).ToArray();
                    AddEvent(newFileName, new PictureEvent() { x = x, pixels = eventsSubArray });
                }
            }
            await SendEvents();
        }

        // Receive from azure queue individual pixels and set the pixel
        // - Create Relay Namespace, Event Subscription
        // - 
        private void ReceiveAndSetPixel()
        {
            this.receivedPixelColumns = this.sentPixelColumns;

            for (int i = 0; i < this.receivedPixelColumns.Count; i++)
            {
                var columnArray = this.receivedPixelColumns[i].Item2;

                for (int j = 0; j < columnArray.Length; j++)
                {
                    targetImage.SetPixel(columnArray[j].x, columnArray[j].y, Color.FromArgb(columnArray[j].argbValue));
                }
            }
        }

        private void ClearPicture()
        {
            this.targetImage = new Bitmap(sourceWidth, sourceHeight);
            pictureBox1.Image = this.targetImage;
            pictureBox1.Refresh();
        }
        
        class PictureEvent
        {
            public int x { get; set; }
            public PixelInfo[] pixels { get; set; }
        }

        class PixelInfo
        {
            public int x;
            public int y;
            public int argbValue;

            public PixelInfo(int x, int y, int argbValue)
            {
                this.x = x;
                this.y = y;
                this.argbValue = argbValue;
            }
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            await this.SendPictureAsPixels();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.ReceiveAndSetPixel();
            pictureBox1.Image = this.targetImage;
            pictureBox1.Refresh();
        }
    }
}
