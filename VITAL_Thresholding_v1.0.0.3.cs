using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using VMS.CA.Scripting;

namespace VMS.IRS.Scripting
{

    ////////////////////////////////////////////////////////////////////////////////
    // VITAL_Thresholding.cs
    //
    // Risks:      Low - Read only
    //              Output used to inform thresholding of functional lung sub-volumes for the VITAL clinical trial 
    //
    //  Author:     John Kipritidis (VITAL Trial QA Committee)
    //  Created:    2024
    //
    //  Purpose:    Display intensity thresholds corresponding to lower, middle and upper thirds (by volume) of a selected structure.
    //              
    //  Input:      A non-empty structure must be selected on the given image (assumed as CT ventilation map)
    //            
    //  Output:     Message window containing information on functional thresholds.
    //              
    //              
    //  Eclipse version:    16.1
    //  Other dependencies: None
    //              
    //  Script version:     1.0.0.3
    //  Changes:            Still to-do: Apply checks on the 3D image to ensure it is a ventilation image, e.g. based on the naming, min/max intensity, other metadata, etc.
    //                      Update 12/12/24: Adjusted histrogram analysis to ignore any "non-ventilation" voxels (i.e. with negative/zero intensity, or below the minimum +ve thresholdable value) within the given structure. This behaviour can be turned on/off using flag at L53. Default behaviour is to switch this behvaiour ON (TRUE).
    //                      Update 24/03/25: Minor adjust to MessageBox wording: It is strongly recommended that functional sub-volumes created as "High Resolution" structures before applying thresholding.
    //                      Update 4/09/25: Adjusted upper threshold for warning message on ventilation image value (warningOnMaximumAllowedValue): Threshold changed from 1000 to 9999.
    //                      
    //  Documentation:      TBD (VITAL Trial QA Committee)
    //  Person/Date:        John K 4/09/2025
    //
    ////////////////////////////////////////////////////////////////////////////////
    ///
    public class Script {

    public Script() {
    }

    public void Execute(ScriptContext context) {


            // Configurable parameters are all set here:
            double fractionalVolumeAtLowerThird = 0.33; // Represents the split point (fractional volume) between lower third and middle third of selected structure
            double fractionalVolumeAtUpperThird = 0.66; // Represents the split point (fractional volume) between middle third and upper third of selected structure
            float minimumAllowedDifferenceBetweenDisplayedThresholdValues = (float)1; // Represents smallest possible difference between threshold values that can be manually typed into Contouring GUI
            int intendedDisplayPrecision = 0; // Integer number of decimal places to use for displayed thresholds (after rounding); must be consistent with the value in L49
            float warningOnMinimumAllowedValue = 0; // Warning will be displayed if Structure contains minimum intensity below this value (may not be a ventilation image) 
            float warningOnMaximumAllowedValue = 9999;  // Warning will be displayed if Structure contains maximum intensity above this value (may not be a ventilation image)
            bool excludeVoxelIntensitiesLessThanOrEqualToZero = true; // If set to true, intensity histogram analysis will exclude any voxels with negative/exactly zero intensity (or below the minimum +ve thresholdable value) within the given structure; refer also to variable on L49

            // There can only be one image selected; i.e. No registration selected.
            if (context.Registration != null)
            {
                MessageBox.Show("To run this script, only ONE image can be opened at a time. Please open (double-click) a single image of interest. Script will now exit.");
                return;
            }

            // Initially check that a non-empty structure is selected, otherwise the script will exit
            if (context.Structure == null
                || (context.Structure as VolumetricStructure).IsEmpty)
            {
                MessageBox.Show("To run this script, you MUST select (click) a non-empty structure. Script will now exit.");
                return;
            }

            // Load up the selected structure as VolumetricStructure type
            VolumetricStructure thisStructure = (context.Structure as VolumetricStructure);

            // Process the vertex coords of the selected structure as a list of Dicom x,y,z (mm):
            float[] theseVertices_Raw = thisStructure.TriangleMesh.Vertices;
            int numberOfVertices = theseVertices_Raw.Length / 6; // each vertex is followed by the normal

            List<VVector> listOfProcessedVertices = new List<VVector>();
            for (int i = 0; i < numberOfVertices; i++)
            {
                int j = i * 6;
                listOfProcessedVertices.Add(new VVector(theseVertices_Raw[j], theseVertices_Raw[j + 1], theseVertices_Raw[j + 2]));
            }
            
            // Now determine 3D bounding box for this structure (will reduce time for testing image voxels as being inside/outside of Structure):
            VVector theseVertices_MinXYZ = new VVector((from VVector thisVertex in listOfProcessedVertices select thisVertex.x).Min(),
                (from VVector thisVertex in listOfProcessedVertices select thisVertex.y).Min(),
                (from VVector thisVertex in listOfProcessedVertices select thisVertex.z).Min());
            VVector theseVertices_MaxXYZ = new VVector((from VVector thisVertex in listOfProcessedVertices select thisVertex.x).Max(),
                (from VVector thisVertex in listOfProcessedVertices select thisVertex.y).Max(),
                (from VVector thisVertex in listOfProcessedVertices select thisVertex.z).Max());

            // Now loop thru every image voxel, collate list of intensity values inside the structure:
            List<float> listOIntensityValuesInStructure = new List<float>();

            bool WereAnyNaNsDetected = false; // Boolean used to track if any null or NaN intensity values detected

            // Load up the current image as a Frame type:
            string thisImageId = context.Image.Id;
            Frame thisImageFrame = context.Image.Frame;

            // Initialise variables used to store fixed image voxels:
            ushort[,] z_slice = new ushort[thisImageFrame.XSize, thisImageFrame.YSize];
            
            // Initialise variables used to store fixed image voxels:
            for (int z = 0; z < Math.Ceiling((decimal)thisImageFrame.ZSize); z++)
            {
                // Image voxels are accessed one slice at a time:
                thisImageFrame.GetVoxels(z, z_slice);
                for (int x = 0; x < Math.Ceiling((decimal)thisImageFrame.XSize); x++)
                {
                    for (int y = 0; y < Math.Ceiling((decimal)thisImageFrame.YSize); y++)
                    {
                        // Note that both the voxel coords (x,y,z as integer) and Dicom coords (x,y,z in mm) are required
                        VVector thisXYZCoordAsVoxelIndex = new VVector((double)x, (double)y, (double)z);
                        VVector thisXYZCoordAsDicomMM = thisImageFrame.VoxelToDicom(thisXYZCoordAsVoxelIndex);

                        if (thisXYZCoordAsDicomMM.x >= theseVertices_MinXYZ.x && thisXYZCoordAsDicomMM.x <= theseVertices_MaxXYZ.x
                            && thisXYZCoordAsDicomMM.y >= theseVertices_MinXYZ.y && thisXYZCoordAsDicomMM.y <= theseVertices_MaxXYZ.y
                            && thisXYZCoordAsDicomMM.z >= theseVertices_MinXYZ.z && thisXYZCoordAsDicomMM.z <= theseVertices_MaxXYZ.z)
                        {
                            // Test if this Dicom coord is inside the structure:
                            if (thisStructure.IsPointInsideSegment(thisXYZCoordAsDicomMM))
                            {
                                // If so, extract the ''Display'' value at this Coord (note, this may differ to raw image intensity):
                                double thisDisplayValue = thisImageFrame.VoxelToDisplayValue(z_slice[x, y]);

                                // Check for NaN value:
                                if (double.IsNaN(thisDisplayValue))
                                    WereAnyNaNsDetected = true; // Do not record value in this case
                                else
                                {
                                    // Apply exclusion of negative/zero intensity values (if applicable); voxel must have greater than zero intensity and above the minimum +ve thresholdable value)
                                    if (!excludeVoxelIntensitiesLessThanOrEqualToZero || (excludeVoxelIntensitiesLessThanOrEqualToZero & (thisDisplayValue > 0) & (thisDisplayValue >= minimumAllowedDifferenceBetweenDisplayedThresholdValues)))
                                    {
                                        // If we have made it to this point, all is good, record the intensity value:
                                        listOIntensityValuesInStructure.Add((float)thisDisplayValue);
                                    }

                                }

                            }
                        }

                    }
                }
            }

            // Now process the list of ventilation values
            if (listOIntensityValuesInStructure.Distinct().Count() >= 3)
            {
                // Sort the list
                listOIntensityValuesInStructure.Sort();

                // Determine split points (array index) for lower third in sorted lists; always provide a unique index above/below the split point to minimize overlap between thresholded regions
                Int32 index_LowerThird_Below = (Int32)Math.Floor((double)listOIntensityValuesInStructure.Count() * fractionalVolumeAtLowerThird);
                Int32 index_LowerThird_Above = (Int32)Math.Ceiling((double)listOIntensityValuesInStructure.Count() * fractionalVolumeAtLowerThird);
                if (index_LowerThird_Below == index_LowerThird_Above)
                    index_LowerThird_Below = index_LowerThird_Above-1;

                // Convert array index to threshold values at the required display precision; make sure the thresholds above/below the boundary zones differ by (at least) the minimum possible difference, so as to minimize overlap between regions
                float threshold_LowerThird_Minimum = (float)Math.Round(listOIntensityValuesInStructure.First(),intendedDisplayPrecision);
                float threshold_LowerThird_Below = (float)Math.Round(listOIntensityValuesInStructure[index_LowerThird_Below],intendedDisplayPrecision);
                float threshold_LowerThird_Above = (float)Math.Round(listOIntensityValuesInStructure[index_LowerThird_Above],intendedDisplayPrecision);
                if (threshold_LowerThird_Below == threshold_LowerThird_Above)
                    threshold_LowerThird_Below = threshold_LowerThird_Above - minimumAllowedDifferenceBetweenDisplayedThresholdValues;

                // Determine split points (array index) for upper third in sorted lists; always provide a unique index above/below the split point to minimize overlap between thresholded regions
                Int32 index_UpperThird_Below = (Int32)Math.Floor((double)listOIntensityValuesInStructure.Count() * fractionalVolumeAtUpperThird);
                Int32 index_UpperThird_Above = (Int32)Math.Ceiling((double)listOIntensityValuesInStructure.Count() * fractionalVolumeAtUpperThird);
                if (index_UpperThird_Below == index_UpperThird_Above)
                    index_UpperThird_Below = index_UpperThird_Above - 1;

                // Convert array index to threshold values at the required display precision; make sure the thresholds above/below the boundary zones differ by (at least) the minimum possible difference, so as to minimize overlap between regions
                float threshold_UpperThird_Maximum = (float)Math.Round(listOIntensityValuesInStructure.Last(),intendedDisplayPrecision);
                float threshold_UpperThird_Below = (float)Math.Round(listOIntensityValuesInStructure[index_UpperThird_Below],intendedDisplayPrecision);
                float threshold_UpperThird_Above = (float)Math.Round(listOIntensityValuesInStructure[index_UpperThird_Above],intendedDisplayPrecision);
                if (threshold_UpperThird_Below == threshold_UpperThird_Above)
                    threshold_UpperThird_Below = threshold_UpperThird_Above - minimumAllowedDifferenceBetweenDisplayedThresholdValues;

                // Define Warning string (if applicable):
                string WarningString = "";

                // Warning if any values outside the expected minimum/maximum values (may not be a ventilation image):
                if (listOIntensityValuesInStructure.Min() < warningOnMinimumAllowedValue || listOIntensityValuesInStructure.Max() > warningOnMaximumAllowedValue)
                    WarningString += "Structure contains intensity values below: " + warningOnMinimumAllowedValue + " or in excess of: " + warningOnMaximumAllowedValue + ". Please ensure this is a valid ventilation image. ";

                if (WereAnyNaNsDetected)
                    WarningString += "One or more NaN values were detected when running the script. This may be because Structure has a bounding box that exceeds dimensions of the image. Please review outputs carefully. ";


                string OutputString = "DISCLAIMER: This script is managed by the VITAL Trial QA Committee and is to be used ONLY in connection with the VITAL clinical trial." + System.Environment.NewLine + System.Environment.NewLine +
                    "Within the selected Structure: ''" + thisStructure.Id + "'', volumetric thirds can be generated by applying intensity thresholds as follows (3D mode, NO smoothing):" + System.Environment.NewLine + System.Environment.NewLine +
                    "\t Lower third: " + Math.Round(threshold_LowerThird_Minimum,intendedDisplayPrecision) + " to " + Math.Round(threshold_LowerThird_Below,intendedDisplayPrecision) + System.Environment.NewLine +
                    "\t Middle third: " + Math.Round(threshold_LowerThird_Above,intendedDisplayPrecision) + " to " + Math.Round(threshold_UpperThird_Below,intendedDisplayPrecision) + System.Environment.NewLine +
                    "\t Upper third: " + Math.Round(threshold_UpperThird_Above,intendedDisplayPrecision) + " to " + Math.Round(threshold_UpperThird_Maximum,intendedDisplayPrecision) + System.Environment.NewLine + System.Environment.NewLine +
                    "Before applying thresholds, please ensure sub-volumes have been created as 'High Resolution' structures. After applying thresholding, sub-volumes MUST undergo boolean intersection with the analysed Structure." + System.Environment.NewLine + System.Environment.NewLine +
                    "If the above instructions are followed, then the estimated volumetric accuracy is 10%. Please copy/paste this window to a text editor for record keeping.";

                if (WarningString != "")
                    OutputString += System.Environment.NewLine + System.Environment.NewLine + "NOTE: One or more warnings were generated as follows: " + WarningString;

                MessageBox.Show(OutputString);
                return;

            }
            else
            {
                MessageBox.Show("Selected Structure contains too few distinct intensity values to determine appropriate thresholds. Script will exit.");
                return;
            }
            

        }

  }

}