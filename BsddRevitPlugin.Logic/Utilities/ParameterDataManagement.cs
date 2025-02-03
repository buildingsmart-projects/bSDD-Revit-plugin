using ASRR.Revit.Core.Utilities;
using Autodesk.Revit.DB;
using BsddRevitPlugin.Logic.IfcJson;
using BsddRevitPlugin.Logic.Model;
using BsddRevitPlugin.Logic.UI.BsddBridge;
using NLog;
using Revit.IFC.Import.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BsddRevitPlugin.Logic.Utilities
{
    public class ParameterDataManagement
    {
        public string _roomName;
        public string _areaName;
        public ParameterDataManagement()
        {

            _roomName = "All Rooms";
            _areaName = "All Area's";
        }

        private static Logger logger = LogManager.GetCurrentClassLogger();

        private static Dictionary<string, bool> _propertyIsInstanceMap = new Dictionary<string, bool>();
        public void GetParametersToCreateAndSet(Document doc, IfcEntity ifcEntity, HashSet<IfcClassification> dictionaryCollection, Dictionary<string, bool> propertyIsInstanceMap, out List<ParameterCreation> parametersToCreate, out Dictionary<string, object> parametersToSet)
        {
            _propertyIsInstanceMap = propertyIsInstanceMap;

            //Get all classifications and properties from the ifcEntity
            var associations = ifcEntity.HasAssociations;
            var isDefinedBy = ifcEntity.IsDefinedBy;

            //Set parameter type and group for the bsdd classification parameters
            ForgeTypeId specType = SpecTypeId.String.Text;

            //Create a list of CLASSIFICATION parameters to create and set
            List<ParameterCreation> classParametersToCreate = new List<ParameterCreation>();
            Dictionary<string, object> classParametersToSet = new Dictionary<string, object>();

            CollectParametersAndValuesFromAssociations(doc, ifcEntity, dictionaryCollection, associations, specType, out classParametersToCreate, out classParametersToSet);

            //Create a list of PROPERTY parameters to create and set
            List<ParameterCreation> propParametersToCreate = new List<ParameterCreation>();
            Dictionary<string, object> propParametersToSet = new Dictionary<string, object>();

            CollectParametersAndValuesFromIsDefinedBy(doc, isDefinedBy, specType, out propParametersToCreate, out propParametersToSet);

            //Create a list of ATTRIBUTE parameters to create or set (ifc types and objecttypes)
            List<ParameterCreation> attParametersToCreate = new List<ParameterCreation>();
            Dictionary<string, object> attParametersToSet = new Dictionary<string, object>();

            CollectParametersAndValuesFromAttributes(doc, ifcEntity, specType, out attParametersToCreate, out attParametersToSet);

            //Combine all parameters to create and set
            parametersToCreate = new List<ParameterCreation>();
            parametersToCreate.AddRange(classParametersToCreate);
            parametersToCreate.AddRange(propParametersToCreate);
            parametersToCreate.AddRange(attParametersToCreate);

            parametersToSet = classParametersToSet
                .Concat(propParametersToSet)
                .Concat(attParametersToSet)
                .GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Last().Value);

            _propertyIsInstanceMap.Clear();
        }

        private static void CollectParametersAndValuesFromAssociations(Document doc,IfcEntity ifcEntity, HashSet<IfcClassification> dictionaryCollection, List<Association> associations, ForgeTypeId specType, out List<ParameterCreation> parametersToCreate, out Dictionary<string, object> parametersToSet)
        {
            //Initialize parameters
            parametersToCreate = new List<ParameterCreation>();
            parametersToSet = new Dictionary<string, object>();
            string bsddParameterName = "";
            string parameterMappedName = null;

            //Add classification parameters to the list
            //Set Revit parameters for each association
            if (associations != null)
            {
                foreach (var association in associations)
                {
                    switch (association)
                    {
                        case IfcClassificationReference ifcClassificationReference:
                            // do something with ifcClassificationReference

                            //Create parameter name for each unique the bsdd classificationReference, this is always added as type parameter!
                            bsddParameterName = ElementsManagerLogic.CreateParameterNameFromIFCClassificationReferenceSourceLocation(ifcClassificationReference);

                            ParameterDataManagement parameterDataManagement = new ParameterDataManagement();
                            if (ifcEntity.Name == parameterDataManagement._areaName || ifcEntity.Name == parameterDataManagement._roomName)
                            {
                                //Add instance parameter
                                parametersToCreate.Add(new ParameterCreation(bsddParameterName + "[Instance]", specType, Parameters.ExistingProjectParameter(doc, bsddParameterName + "[Instance]"), true));
                            }
                            else
                            {
                                //Add type parameter
                                parametersToCreate.Add(new ParameterCreation(bsddParameterName, specType, Parameters.ExistingProjectParameter(doc, bsddParameterName), false));
                            }
                            parametersToSet.Add(bsddParameterName, ifcClassificationReference.Identification + ":" + ifcClassificationReference.Name);
                            
                            dictionaryCollection.Add(ifcClassificationReference.ReferencedSource);

                            //Get mapped parametername (stored in the documents DataStorage)
                            parameterMappedName = GetMappedParameterName(ifcClassificationReference);

                            if (parameterMappedName != "" && parameterMappedName != null)
                            {
                                parametersToSet.Add(parameterMappedName, ifcClassificationReference.Identification);
                            }
                            break;

                        case IfcMaterial ifcMaterial:
                            // do something with ifcMaterial
                            break;
                    }
                }
            }
        }

        private static void CollectParametersAndValuesFromIsDefinedBy(Document doc, List<IfcPropertySet> isDefinedBy, ForgeTypeId specType, out List<ParameterCreation> parametersToCreate, out Dictionary<string, object> parametersToSet)
        {
            //Initialize parameters
            parametersToCreate = new List<ParameterCreation>();
            parametersToSet = new Dictionary<string, object>();
            string bsddParameterName = "";
            string parameterMappedName = "";

            //Add property parameters to the list
            if (isDefinedBy != null)
            {
                foreach (var propertySet in isDefinedBy)
                {
                    foreach (var property in propertySet.HasProperties)
                    {
                        if (property.Type == null)
                        {
                            continue;
                        }
                        object value = null;
                        if (property.Type == "IfcPropertySingleValue")
                        {
                            var propertySingleValue = property as IfcPropertySingleValue;
                            if (propertySingleValue.NominalValue == null)
                            {
                            }
                            else
                            {
                                value = GetParameterValueInCorrectDatatype(propertySingleValue.NominalValue);
                                specType = GetParameterTypeFromProperty(propertySingleValue.NominalValue);

                            }
                        }
                        else if (property.Type == "IfcPropertyEnumeratedValue")
                        {
                            var propertyEnumeratedValue = property as IfcPropertyEnumeratedValue;
                            if (propertyEnumeratedValue.EnumerationValues == null || propertyEnumeratedValue.EnumerationValues.Count == 0)
                            {
                            }
                            else
                            {
                                var enumerationValue = propertyEnumeratedValue.EnumerationValues.First();
                                value = GetParameterValueInCorrectDatatype(enumerationValue);
                                specType = GetParameterTypeFromProperty(enumerationValue);

                            }
                        }

                        //check if instance or type
                        bool isInstance = false;
                        string propertyName = property.Name;
                        string propertySetPropertyName = propertySet.Name + "/" + property.Name;


                        if (_propertyIsInstanceMap.ContainsKey(propertyName))
                        {
                             _propertyIsInstanceMap.TryGetValue(propertyName, out isInstance);
                        }
                        else if (_propertyIsInstanceMap.ContainsKey(propertySetPropertyName))
                        {
                            _propertyIsInstanceMap.TryGetValue(propertySetPropertyName, out isInstance);
                        }

                        bsddParameterName = CreateParameterNameFromPropertySetAndProperty(propertySet.Name, property);
                        parametersToCreate.Add(new ParameterCreation(bsddParameterName, specType, Parameters.ExistingProjectParameter(doc, bsddParameterName), isInstance));
                        
                        //Never set instance parameters untill IfcValue is implemented
                        if (!isInstance)
                        {
                            parametersToSet.Add(bsddParameterName, value);
                        }
                    }
                }
            }
        }

        private static void CollectParametersAndValuesFromAttributes(Document doc, IfcEntity ifcEntity, ForgeTypeId specType, out List<ParameterCreation> parametersToCreate, out Dictionary<string, object> parametersToSet)
        {
            //Initialize parameters
            parametersToCreate = new List<ParameterCreation>();
            parametersToSet = new Dictionary<string, object>();

            //Add attribute parameters to the list
            //Set Revit parameters for each attribute

            if (ifcEntity.Type != null)
            {
                parametersToSet.Add("Export Type to IFC As", ifcEntity.Type);

                if (ifcEntity.PredefinedType != null)
                {

                    parametersToSet.Add("Type IFC Predefined Type", ifcEntity.PredefinedType);
                }
                else
                {
                    parametersToSet.Add("Type IFC Predefined Type", "");

                }
            }
            if (ifcEntity.Description != null)
            {
                if (ifcEntity.Description != "...")
                {
                    parametersToSet.Add("Description", ifcEntity.Description);
                }
            }
            ParameterDataManagement parameterDataManagement = new ParameterDataManagement();
            if (ifcEntity.Name == parameterDataManagement._areaName || ifcEntity.Name == parameterDataManagement._roomName)
            {
                //This is always added as instance parameter!
                string objectTypeParamName = "IfcObjectType";
                parametersToCreate.Add(new ParameterCreation(objectTypeParamName, SpecTypeId.String.Text, Parameters.ExistingProjectParameter(doc, objectTypeParamName), true));
            }
            if (ifcEntity.ObjectType != null)
            {
                //This is always added as type parameter!
                string objectTypeParamName = "IfcObjectType[Type]";
                parametersToCreate.Add(new ParameterCreation(objectTypeParamName, SpecTypeId.String.Text, Parameters.ExistingProjectParameter(doc, objectTypeParamName), false));
                parametersToSet.Add(objectTypeParamName, ifcEntity.ObjectType);
            }

        }

        /// <summary>
        /// Converts the value of the given IFC property to the correct datatype.
        /// </summary>
        /// <param name="propertyValue">The IFC property to convert.</param>
        /// <returns>The converted value, or a default value if the conversion fails.</returns>
        public static dynamic GetParameterValueInCorrectDatatype(IfcValue propertyValue)
        {
            dynamic value = propertyValue.Value;

            if (value != null)
            {

                // Parse value to correct datatype
                switch (propertyValue.Type)
                {
                    case "IfcBoolean":
                        value = TryConvertValue(value, new Func<dynamic, dynamic>(v => (bool)v ? 1 : 0), 0);
                        break;
                    case "IfcInteger":
                        value = TryConvertValue(value, new Func<dynamic, dynamic>(v => Convert.ToInt32(v)), 0);
                        break;
                    case "IfcReal":
                        value = CoordinateUtilities.ConvertMmToFeet(TryConvertValue(value, new Func<dynamic, dynamic>(v => Convert.ToDouble(v)), 0));
                        break;
                    case "IfcDate":
                    case "IfcDateTime":
                        //TODO: Check what seems to be a valid DateTime to get and convert
                        value = TryConvertValue(value, new Func<dynamic, dynamic>(v => Convert.ToDateTime(v).ToString()), "");
                        break;
                    default:
                        // IfcText, IfcLabel, IfcIdentifier or Default
                        value = TryConvertValue(value, new Func<dynamic, dynamic>(v => v.ToString()), "");
                        break;
                }
            }

            return value;
        }


        /// <summary>
        /// Tries to convert a value using the given conversion function.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="convert">The conversion function to use.</param>
        /// <param name="defaultValue">The default value to return if the conversion fails.</param>
        /// <returns>The converted value, or the default value if the conversion fails.</returns>
        public static dynamic TryConvertValue(dynamic value, Func<dynamic, object> convert, dynamic defaultValue)
        {
            try
            {
                return value != null ? convert(value) : defaultValue;
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Determines the Revit parameter type from an IFC property.
        /// </summary>
        /// <param name="property">The IFC property.</param>
        /// <returns>The corresponding Revit parameter type.</returns>
        public static ForgeTypeId GetParameterTypeFromProperty(IfcValue ifcValue)
        {
            // The type of the nominal value in the IFC property
            string valueType = ifcValue.Type;

            // Map the IFC type to the corresponding Revit parameter type
            switch (valueType)
            {
                case "IfcBoolean":
                    // Map IfcBoolean to Revit's YesNo type
                    return SpecTypeId.Boolean.YesNo;

                case "IfcInteger":
                    // Map IfcInteger to Revit's Integer type
                    return SpecTypeId.Int.Integer;

                case "IfcReal":
                    // Map IfcReal to Revit's Number type
                    return SpecTypeId.Number;

                case "IfcDate":
                case "IfcDateTime":
                    // Revit does not support date types, so map IfcDate and IfcDateTime to Revit's Text type
                    return SpecTypeId.String.Text;

                case "IfcText":
                case "IfcLabel":
                case "IfcIdentifier":
                    // Map IfcText to Revit's Text type
                    return SpecTypeId.String.Text;

                default:
                    // If the IFC type is not recognized, default to Revit's Text type
                    return SpecTypeId.String.Text;
            }
        }

        // Revit UI Project Parameter Types:
        //
        // Text             The parameter data should be interpreted as a string of text.
        // Integer          The parameter data should be interpreted as a whole number, positive or negative.
        // Angle            The parameter data represents an angle.
        // Area	            The parameter data represents an area.
        // Cost per Area    ???
        // Distance         ???
        // Length	        The parameter data represents a length. (in feet)
        // MassDensity	    The data value will be represented as a MassDensity.
        // Number	        The parameter data should be interpreted as a real number, possibly including decimal points.
        // Rotation Angle   The data value will be represented as a Rotation.
        // Slope	        The data value will be represented as a Slope.
        // Speed            ???
        // Time             ???
        // Volume           The parameter data represents a volume.
        // Currency         The data value will be represented as a Currency.
        // URL	            A text string that represents a web address.              
        // Material	        The value of this property is considered to be a material.
        // Fill Pattern	    ???
        // Image	        The value of this parameter is the id of an image.
        // YesNo	        A boolean value that will be represented as Yes or No.
        // MultilineText	The value of this parameter will be represented as multiline text.

        // IFC simple value data types:
        // https://standards.buildingsmart.org/IFC/RELEASE/IFC4_3/HTML/lexical/IfcSimpleValue.htm
        //
        // IfcBinary            BINARY IfcBinary is a defined type of simple data type BINARY which may be used to encode binary data such as embedded textures.
        // IfcBoolean           BOOLEAN (TRUE or FALSE)
        // IfcDate              STRING (YYYY-MM-DD)
        // IfcDateTime          STRING (YYYY-MM-DDThh:mm:ss)
        // IfcDuration          STRING
        // IfcIdentifier        STRING(255) for identification purposes. An identifier is an alphanumeric string which allows an individual thing to be identified. It may not provide natural-language meaning. it should be restricted to max. 255 characters.
        // IfcInteger           INTEGER In principle, the domain of IfcInteger (being an Integer) is all integer numbers.
        // IfcLabel             STRING(255) A label is the term by which something may be referred to. It is a string which represents the human-interpretable name of something and shall have a natural-language meaning. it should be restricted to max. 255 characters.
        // IfcLogical           LOGICAL (TRUE, FALSE or UNKNOWN)
        // IfcPositiveInteger   IfcInteger > 0
        // IfcReal              REAL In principle, the domain of IfcReal (being a Real) is all rational, irrational and scientific real numbers.
        // IfcText              STRING A text is an alphanumeric string of characters which is intended to be read and understood by a human being. It is for information purposes only.
        // IfcTime              STRING (hh:mm:ss)
        // IfcTimeStamp         INTEGER
        // IfcURIReference      STRING

        // bSDD Property DataTypes:
        // https://github.com/buildingSMART/bSDD/blob/master/Documentation/bSDD%20JSON%20import%20model.md#property
        //
        // Boolean          IfcBoolean
        // Character        IfcText
        // Integer          IfcInteger
        // Real             IfcReal
        // String           IfcText
        // Time             IfcDateTime

        /// <summary>
        /// Creates a Revit bSDD parameter name for the from the given peropertyset and property.
        /// </summary>
        /// <param name="uri">The URI to create the parameter name from.</param>
        /// <returns>The parameter name created from the URI.</returns>
        public static string CreateParameterNameFromPropertySetAndProperty(string propertySet, IfcProperty property)
        {
            string parameterName;

            //TODO: add parametermapping for all IFC properties (https://github.com/Autodesk/revit-ifc/blob/master/Source/RevitIFCTools/IFC%20Shared%20Parameters-RevitIFCBuiltIn_ALL.txt)
            //NOTE: We generate our own parameterguid using the string name (the bsdd code) without the pset (FA not Pset_SlabCommon.LoadBearing (that has guid 92825b2b-4c88-4474-b84c-09c03ed3783a) but LoadBearing)
            if (!IfcParameterMappings.Mappings.TryGetValue(property.Specification, out parameterName))
            {
                parameterName = $"bsdd/prop/{propertySet}/{property.Name}";
            }

            return parameterName;
        }

        public static string GetMappedParameterName(IfcClassificationReference ifcClassificationReference)
        {
            Uri refSourceLocation = ifcClassificationReference.ReferencedSource.Location;

            if (GlobalBsddSettings.bsddsettings.MainDictionary.IfcClassification.Location == refSourceLocation)
            {
                var paramMapping = GlobalBsddSettings.bsddsettings.MainDictionary.ParameterMapping;
                if (paramMapping == "")
                {
                    return null;

                }
                else
                {
                    return GlobalBsddSettings.bsddsettings.MainDictionary.ParameterMapping;
                }
            }
            else
            {
                foreach (var filterDictionary in GlobalBsddSettings.bsddsettings.FilterDictionaries)
                {
                    if (filterDictionary.IfcClassification.Location == refSourceLocation)
                    {
                        return filterDictionary.ParameterMapping;
                    }
                }
            }
            return null;
        }

        public static Dictionary<string,bool> GetProjectParameterTypes(Document doc)
        {
            Dictionary<string, bool> projectParameterTypes = new Dictionary<string, bool>();

            ////CODE BELOW GETS ALL INTERNAL DEFINITIONS, ALSO FAMILY ONES (SAVED FOR LATER USE):
            //var projectParameters = new FilteredElementCollector(doc)
            //    .OfClass(typeof(ParameterElement))
            //    .Cast<ParameterElement>()
            //    .Where(p => p.GetDefinition() is InternalDefinition)
            //    .Select(p => p.GetDefinition() as InternalDefinition)
            //    .ToList();
            //foreach (var projectParameter in projectParameters)
            //{
            //    projectParameterTypes.Add(projectParameter.Name, false);
            //}

            BindingMap bindingMap = doc.ParameterBindings;
            DefinitionBindingMapIterator it = bindingMap.ForwardIterator();

            while (it.MoveNext())
            {
                if (it.Key is InternalDefinition def)
                {
                    string parameterName = def.Name;
                    bool isInstance = false;

                    if (IfcParameterMappings.Mappings.ContainsValue(parameterName))
                    {
                        if (it.Current is InstanceBinding)
                        {
                            isInstance = true;
                        }
                        if (!projectParameterTypes.ContainsKey(parameterName))
                        {
                            projectParameterTypes.Add(parameterName, isInstance);
                        }
                    }
                    else if (parameterName.StartsWith("bsdd/prop/"))
                    {
                        string parameterPropertyName = parameterName.Substring(parameterName.LastIndexOf('/') + 1);

                        if (it.Current is InstanceBinding)
                        {
                            isInstance = true;
                        }
                        if (!projectParameterTypes.ContainsKey(parameterPropertyName))
                        {
                            projectParameterTypes.Add(parameterPropertyName, isInstance);
                        }
                    }
                }
            }
            //Disable name to be editable in UI:
            projectParameterTypes.Add("Attributes/Name", true);

            return projectParameterTypes;
        }

    }
}
