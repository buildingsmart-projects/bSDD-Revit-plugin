﻿using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BIM.IFC.Export.UI;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Web.Script.Serialization;
using System.IO;
using NLog;
using System.Reflection;

namespace BsddRevitPlugin.Logic.UI.Services
{
    public abstract class IfcExportService : IIfcExportService
    {
        private const string ifcExportFieldName = "IFCExportConfigurationMap";
        private const string s_configMapField = "MapField";
        private const string bsddExportConfigurationName = "Bsdd export settings";

        // bSDD plugin settings schema ID
        private Schema m_jsonSchema = GetExportSchema();
        private static Guid s_jsonSchemaId = new Guid("c2a3e6fe-ce51-4f35-8ff1-20c34567b687");

        protected abstract void SetExportLinkedFiles(IFCExportConfiguration configuration);
        protected abstract void SetActiveViewId(IFCExportConfiguration configuration, Document document);
        protected abstract void SetActivePhaseId(IFCExportConfiguration configuration);

        /// <summary>
        /// Retrieves all BSDD parameters from the specified document.
        /// </summary>
        /// <param name="document">The document to retrieve the parameters from.</param>
        /// <returns>A list of BSDD parameters.</returns>
        public IList<Parameter> GetAllBsddParameters(Document document)
        {
            FilteredElementCollector typeCollector = new FilteredElementCollector(document).WhereElementIsElementType();
            FilteredElementCollector instanceCollector = new FilteredElementCollector(document).WhereElementIsNotElementType();
            IList<Parameter> parameters = new List<Parameter>();
            IList<Element> allElements = typeCollector.ToList();
            allElements = allElements.Concat(instanceCollector.ToList()).ToList();

            foreach (Element element in allElements)
            {
                foreach (Parameter parameter in element.Parameters)
                {
                    if (parameter.Definition.Name.StartsWith("bsdd/prop/") && !parameters.Any(p => p.Definition.Name == parameter.Definition.Name && p.StorageType == parameter.StorageType))
                    {
                        parameters.Add(parameter);
                    }
                }
            }

            return parameters;
        }

        /// <summary>
        /// Retrieves the BSDD properties as a parameter file.
        /// </summary>
        /// <param name="document">The Revit document.</param>
        /// <param name="mappingParameterFilePath">The file path of the active mapping parameter file.</param>
        /// <returns>The file path of the combined parameter file.</returns>
        public string GetBsddPropertiesAsParameterfile(Document document, string mappingParameterFilePath, IFCVersion ifcVersion)
        {
            // Initialize a string to hold all parameters starting with bsdd for the Export User Defined Propertysets
            string add_BSDD_UDPS = null;


            IList<Parameter> bsddParameters = GetAllBsddParameters(document);

            // Organize the BSDD parameters by property set name
            var parametersMappedByPropertySet = RearrageParamatersForEachPropertySet(bsddParameters);

            // Loop through all property sets
            foreach (var parameters in parametersMappedByPropertySet)
            {


                // Format:
                // #
                // #
                // PropertySet:	<Pset Name>	I[nstance]/T[ype]	<element list separated by ','>
                // #
                // <Property Name 1>	<Data type>	<[opt] Revit parameter name, if different from IFC>
                // <Property Name 2>	<Data type>	<[opt] Revit parameter name, if different from IFC>
                // ...
                // Add the initial format for the property set to the string
                add_BSDD_UDPS += Environment.NewLine + $"#\n#\nPropertySet:\t{parameters.Key}\tT\tIfcElementType, IfcSpaceType, IfcObject, IfcObjectType, IfcSite, IfcSiteType\n#\n#\tThis propertyset has been generated by the BSDD Revit plugin\n#";

                // Loop through all parameters
                foreach (Parameter p in parameters.Value)
                {
                    string parameterName = p.Definition.Name.ToString();
                    string[] parts = parameterName.Split('/');

                    // Get the property set name
                    string propertySetName = parts.Length >= 4 ? parts[3] : parameterName;
                    add_BSDD_UDPS += $"\n\t{propertySetName}\t";

                    //datatypes convert 
                    //C# byte, sbyte, short, ushort, int, uint, long, ulong, float, double, decimal, char, bool, object, string, DataTime
                    //Ifc Area, Boolean, ClassificationReference, ColorTemperature, Count, Currency, 
                    //ElectricalCurrent, ElectricalEfficacy, ElectricalVoltage, Force, Frequency, Identifier, 
                    //Illuminance, Integer, Label, Length, Logical, LuminousFlux, LuminousIntensity, 
                    //NormalisedRatio, PlaneAngle, PositiveLength, PositivePlaneAngle, PositiveRatio, Power, 
                    //Pressure, Ratio, Real, Text, ThermalTransmittance, ThermodynamicTemperature, Volume, 
                    //VolumetricFlowRate

                    // Convert the parameter data type to the corresponding IFC data type and add it to the string
                    string dataType = GetDataType(p);
                    add_BSDD_UDPS += $"{dataType}\t{parameterName}";
                }
            }

            if (ifcVersion == IFCVersion.IFC4x3)
            {
                //Find the UDPS -Quantities-4x3.txt file to export Quantities in IFC 4x3
                string currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string settingsFilePath = currentPath + "\\Resources\\UDPS-Quantities-4x3.txt";

                // Add all text from the settingsfilepath file to the add_BSDD_UDPS string
                add_BSDD_UDPS += File.ReadAllText(settingsFilePath);
            }

            // Create a new temp file for the user defined parameter mapping file
            string randomFileName = Path.GetRandomFileName();
            string combinedParameterFilePath = Path.Combine(Path.GetTempPath(), randomFileName.Remove(randomFileName.Length - 4) + ".txt");

            // Copy user defined parameter mapping file to temp file
            if (File.Exists(mappingParameterFilePath))
            {
                File.Copy(mappingParameterFilePath, combinedParameterFilePath, true);
            }

            // Write the BSDD properties to the temp file
            using (StreamWriter writer = new StreamWriter(combinedParameterFilePath, true))
            {
                writer.WriteLine(add_BSDD_UDPS);
            }

            return combinedParameterFilePath;
        }

        /// <summary>
        /// Gets the data type of a parameter.
        /// </summary>
        /// <param name="parameter">The parameter.</param>
        /// <returns>The data type of the parameter.</returns>
        private string GetDataType(Parameter parameter)
        {
            switch (parameter.StorageType.ToString())
            {
                case "String":
                    return "Text";
                case "Double":
                    return "Real";
                case "Integer":
                    return parameter.Definition.GetDataType().TypeId == "autodesk.spec:spec.bool-1.0.0" ? "Boolean" : "Integer";
                default:
                    return parameter.StorageType.ToString();
            }
        }

        /// <summary>
        /// Rearranges the given list of parameters into a dictionary, grouping them by property set name.
        /// </summary>
        /// <param name="parameters">The list of parameters to be rearranged.</param>
        /// <returns>A dictionary where the keys are property set names and the values are lists of parameters belonging to each property set.</returns>
        public Dictionary<string, IList<Parameter>> RearrageParamatersForEachPropertySet(IList<Parameter> parameters)
        {
            Dictionary<string, IList<Parameter>> propertySetGroups = new Dictionary<string, IList<Parameter>>();

            foreach (Parameter p in parameters)
            {
                string[] parts = p.Definition.Name.Split('/');

                if (parts.Length >= 3)
                {
                    string propertySetName = parts[2];

                    if (!propertySetGroups.ContainsKey(propertySetName))
                    {
                        propertySetGroups[propertySetName] = new List<Parameter>();
                    }

                    propertySetGroups[propertySetName].Add(p);
                }
            }

            return propertySetGroups;
        }

        /// <summary>
        /// Retrieves or creates the schema for the BSDD plugin settings.
        /// </summary>
        /// <returns>
        /// The schema for the BSDD plugin settings. If the schema does not exist, it is created.
        /// </returns>
        private static Schema GetExportSchema()
        {
            Schema schema = Schema.Lookup(s_jsonSchemaId);
            if (schema == null)
            {
                SchemaBuilder schemaBuilder = new SchemaBuilder(s_jsonSchemaId);
                schemaBuilder.SetSchemaName(ifcExportFieldName);
                schemaBuilder.AddSimpleField(s_configMapField, typeof(string));
                schema = schemaBuilder.Finish();
            }
            return schema;
        }

        /// <summary>
        /// Set and retrieve the bSDD IFC export configuration.
        /// </summary>
        public IFCExportConfiguration GetOrSetBsddConfiguration(Document document)
        {
            IList<DataStorage> savedConfigurations = GetSavedConfigurations(document, m_jsonSchema);

            if (savedConfigurations.Count > 0)
            {
                foreach (var configurationData in savedConfigurations)
                {

                    Entity configEntity = configurationData.GetEntity(m_jsonSchema);
                    string configData = configEntity.Get<string>(s_configMapField);

                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    ser.RegisterConverters(new JavaScriptConverter[] { new IFCExportConfigurationConverter() });
                    IFCExportConfiguration configuration = ser.Deserialize<IFCExportConfiguration>(configData);

                    if (configuration.Name == bsddExportConfigurationName)
                    {
                        return configuration;
                    }
                }

            }

            return CreateNewBsddConfigurationInDataStorage(document);
        }

        /// <summary>
        /// Set default bSDD export configuration in Revit DataStorage.
        /// </summary>
        public IFCExportConfiguration CreateNewBsddConfigurationInDataStorage(Autodesk.Revit.DB.Document document)
        {
            IFCExportConfiguration configuration = GetDefaultExportConfiguration(document);

            using (Transaction transaction = new Transaction(document, "Create DataStorage"))
            {
                transaction.Start();

                DataStorage configStorage;
                configStorage = DataStorage.Create(document);
                Entity mapEntity = new Entity(m_jsonSchema);
                string configData = configuration.SerializeConfigToJson();
                mapEntity.Set<string>(s_configMapField, configData);
                configStorage.SetEntity(mapEntity);

                transaction.Commit();
            }

            return configuration;
        }



        /// <summary>
        /// Create a new Revit bSDD IFC export configuration.
        /// </summary>
        public IFCExportConfiguration GetDefaultExportConfiguration(Autodesk.Revit.DB.Document document)
        {
            //Create an instance of the IFC Export Configuration Class
            IFCExportConfiguration configuration = IFCExportConfiguration.CreateDefaultConfiguration();

            configuration.Name = bsddExportConfigurationName;

            //Apply the IFC Export Setting (Those are equivalent to the Export Setting in the IFC Export User Interface)
            //General
            //configuration.IFCVersion = IFCVersion.IFC2x3CV2;
            configuration.IFCVersion = IFCVersion.IFC4x3;
            configuration.ExchangeRequirement = 0;
            configuration.IFCFileType = 0;
            configuration.SpaceBoundaries = 0;
            configuration.SplitWallsAndColumns = false;

            ////Additional Content
            SetExportLinkedFiles(configuration);
            SetActiveViewId(configuration, document);
            SetActivePhaseId(configuration);

            //configuration.ActivePhaseId = (int)ElementId.InvalidElementId.Value;
            ////configuration.ActivePhaseId = document.ActiveView.get_Parameter(BuiltInParameter.VIEW_PHASE).AsElementId()?.Value ?? ElementId.InvalidElementId.Value;

            configuration.VisibleElementsOfCurrentView = true;
            configuration.ExportRoomsInView = false;
            configuration.IncludeSteelElements = true;
            configuration.Export2DElements = false;

            //Property Sets
            configuration.ExportInternalRevitPropertySets = false;
            configuration.ExportIFCCommonPropertySets = true;
            configuration.ExportBaseQuantities = true;
            //configuration material prop sets
            configuration.ExportSchedulesAsPsets = false;
            configuration.ExportSpecificSchedules = false;
            configuration.ExportUserDefinedPsets = false;
            configuration.ExportUserDefinedPsetsFileName = "";
            configuration.ExportUserDefinedParameterMapping = false;
            configuration.ExportUserDefinedParameterMappingFileName = "";

            //Level of Detail
            configuration.TessellationLevelOfDetail = 0.5;

            //Advanced
            configuration.ExportPartsAsBuildingElements = false;
            configuration.ExportSolidModelRep = false;
            configuration.UseActiveViewGeometry = false;
            configuration.UseFamilyAndTypeNameForReference = false;
            configuration.Use2DRoomBoundaryForVolume = false;
            configuration.IncludeSiteElevation = false;
            configuration.StoreIFCGUID = true;
            configuration.ExportBoundingBox = false;
            configuration.UseOnlyTriangulation = false;
            configuration.UseTypeNameOnlyForIfcType = false;
            configuration.UseVisibleRevitNameAsEntityName = false;

            //Geographic Reference

            return configuration;
        }

        /// <summary>
        /// Retrieves the list of saved configurations from the document that match the given schema.
        /// </summary>
        /// <param name="document">The Revit document.</param>
        /// <param name="schema">The schema to match.</param>
        /// <returns>A list of DataStorage objects that contain valid entities matching the specified schema.</returns>
        public static IList<DataStorage> GetSavedConfigurations(Document document, Schema schema)
        {
            FilteredElementCollector collector = new FilteredElementCollector(document);
            collector.OfClass(typeof(DataStorage));
            Func<DataStorage, bool> hasTargetData = ds => (ds.GetEntity(schema) != null && ds.GetEntity(schema).IsValid());

            return collector.Cast<DataStorage>().Where<DataStorage>(hasTargetData).ToList<DataStorage>();
        }
    }
}
