﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartDormitory.Data.Context;
using SmartDormitory.Data.Models;
using SmartDormitory.Services.External.Contracts;
using SmartDormitory.Services.External.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmartDormitory.Services.External
{
    public class RestClientService : IRestClientService
    {
        private readonly HttpClient _client;
        private readonly DormitoryContext context;

        public RestClientService(DormitoryContext context, HttpClient client)
        {
            this.context = context;
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.BaseAddress = new Uri("http://telerikacademy.icb.bg/api/sensor");
            this._client.DefaultRequestHeaders.Add("auth-token", "8e4c46fe-5e1d-4382-b7fc-19541f7bf3b0");
        }

        public IEnumerable<SensorDTO> GetAllSensorsAsync(string apiRoute, string authToken)
        {
            var response = this._client.GetAsync(_client.BaseAddress + "/" + apiRoute).GetAwaiter().GetResult();

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            return JsonConvert.DeserializeObject<IEnumerable<SensorDTO>>(json);
        }

        public SensorInfoDTO GetSensorById(string authToken,string id)
        {
            var response = this._client.GetAsync(_client.BaseAddress + "/" + id).GetAwaiter().GetResult();

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            return JsonConvert.DeserializeObject<SensorInfoDTO>(json);
        }

        public IDictionary<string, Sensor> InitialSensorLoad()
            {
                var result = new Dictionary<string, Sensor>();

                var sensors = this.context.Sensors;

                foreach (var sensor in sensors)
                {
                    if (!result.ContainsKey(sensor.ApiId.ToString()))
                    {
                        result.Add(sensor.ApiId.ToString(), sensor);
                    }
                }
                return result;
            }

        public IDictionary<string, Sensor> CheckForNewSensor(IDictionary<string, Sensor> listOfSensors)
        {
            var response = GetAllSensorsAsync("all", "8e4c46fe-5e1d-4382-b7fc-19541f7bf3b0");

            foreach (var sensor in response)
            {
                string sensorId = sensor.SensorId;

                if (!listOfSensors.ContainsKey(sensorId))
                {
                    var measureType = sensor.MeasureType;
                    string tag = sensor.Tag;
                    string typeTag = tag.Substring(0, tag.IndexOf("Sensor"));
                    var measure = "To Do!";
                    //var measure = CheckForNewMeasureType(measureType);
                    var type = CheckForNewSensorType(typeTag);
                    listOfSensors.Add(sensorId, AddNewSensoreToDatabase(sensor));
                }
            }
            return listOfSensors;
        }

        private Sensor AddNewSensoreToDatabase(SensorDTO sensorToAdd)
        {
            string sensorId = sensorToAdd.SensorId;
            string description = sensorToAdd.Description;
            var extractedValues = GetMinMaxValues(description);
            string tag = sensorToAdd.Tag;
            string measureType = sensorToAdd.MeasureType;

            int minPollInterval = int.TryParse
                (sensorToAdd.MinPollingIntervalInSeconds.ToString(), out int number)
                ? number : 10;

            var newSensor = new Sensor()
            {
                ApiId = sensorId,
                Description = description,
                PoolInterval = minPollInterval,
                MeasurmentType = measureType,
                ValueRangeMax = Math.Max(extractedValues[0], extractedValues[1]),
                ValueRangeMin = Math.Min(extractedValues[0], extractedValues[1])
            };

            this.context.Add(newSensor);
            this.context.SaveChanges();

            return newSensor;
        }

        private SensorType CheckForNewSensorType(string typeTag)
        {
            var result = this.context.SensorTypes.Where(s => s.Type == typeTag);
            SensorType newSensorType = null;

            if (result.Count() == 0)
            {
                newSensorType = new SensorType()
                {
                    Type = typeTag
                };
                this.context.SensorTypes.Add(newSensorType);
                this.context.SaveChanges();
            }
            return newSensorType;
        }

        public IDictionary<string, Sensor> UpdateSensors(IDictionary<string, Sensor> listOfSensors)
        {
            ICollection<Sensor> sensorForUpdate = new List<Sensor>();

            bool isAnySensorForUpdate = false;
            foreach (var sensor in listOfSensors.Values)
            {
                if (DateTime.Parse(sensor.TimeStamp.ToString()).AddSeconds(sensor.PoolInterval) < DateTime.Now)
                {
                    var response = GetSensorById("8e4c46fe-5e1d-4382-b7fc-19541f7bf3b0", sensor.ID.ToString());

                    sensor.TimeStamp = response.TimeStamp;
                    sensor.Value = response.Value;

                    sensorForUpdate.Add(sensor);

                    isAnySensorForUpdate = true;
                }
            }
            if (isAnySensorForUpdate)
            {
                context.UpdateRange(sensorForUpdate);
                context.SaveChanges();
            }

            return listOfSensors;
        }

        private double[] GetMinMaxValues(string input)
        {
            var numbers = Regex.Matches(input, @"(\+| -)?(\d+)(\,|\.)?(\d*)?");
            var result = new double[] { 0, 1 };
            if (numbers.Count > 0)
            {
                double.TryParse(numbers[0].ToString(), out result[0]);
                double.TryParse(numbers[1].ToString(), out result[1]);
            }
            return result;
        }
    }
}
