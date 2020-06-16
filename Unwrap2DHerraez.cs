using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using System.Diagnostics;


namespace Unwrap2DHerraez
{
    public unsafe class Unwrap2DHerraez
    {
        static readonly float PI = (float)Math.PI;
        static readonly float TWOPI = 2 * (float)Math.PI;

        // Define structs used throughout
        public struct params_t
        {
            public float mod;
            public int no_of_edges;
        }

        public struct PIXELM
        {
            public int increment; //No. of 2pi to add to the pixel to unwrap it
            public int number_of_pixels_in_group;
            public float value; // Pixel value
            public float reliability;
            //public byte input_mask; // 0 value is masked, 1 value is unmasked
            //public byte extended_mask; // 0 value is masked, 1 value is unmasked
            public int group_number;
            public int new_group;
            public PIXELM* head; // The "pixel address" of the head of the linked-list
            public PIXELM* last; // The "pixel address" of the last element in this group
            public PIXELM* next; // The "pixel address" of the next element in this group
        }

        public struct EDGE
        {
            public float reliab; // Reliability of the edge depends on bordering pixels
            public PIXELM* pointer_1; // Pixel Pointer to the first bordering pixel
            public PIXELM* pointer_2; // Pixel Pointer to the second bordering pixel
            public int increment; // Number of 2*pi to add to one of the pixels to unwrap it in the wrt the other
        }

        // Start Initialize Pixels
        // Initially every pixel is assumed to belong to a group of only itself
        // Initialize with random reliabilities
        private static void initialisePIXELs(float* wrapped_image, PIXELM* pixel, int image_width, int image_height)
        {
            Random random = new Random();
            PIXELM* pixel_pointer = pixel;
            float* wrapped_image_pointer = wrapped_image;

            for (int i = 0; i < image_height; i++)
            {
                for (int j = 0; j < image_width; j++)
                {
                    pixel_pointer->increment = 0;
                    pixel_pointer->number_of_pixels_in_group = 1;
                    pixel_pointer->value = *wrapped_image_pointer;
                    pixel_pointer->reliability = random.Next(0, Int32.MaxValue);
                    //pixel_pointer->input_mask = *input_mask_pointer;
                    //pixel_pointer->extended_mask = *extended_mask_pointer; 
                    pixel_pointer->head = pixel_pointer;
                    pixel_pointer->last = pixel_pointer;
                    pixel_pointer->next = null;
                    pixel_pointer->new_group = 0;
                    pixel_pointer->group_number = -1;
                    pixel_pointer++;
                    wrapped_image_pointer++;
                    //extended_mask_pointer++;
                }
            }
        }

        // gamma function in the paper
        private static float wrap(float pixel_value)
        {
            float wrapped_pixel_value;
            if (pixel_value > PI)
                wrapped_pixel_value = pixel_value - TWOPI;
            else if (pixel_value < -PI)
                wrapped_pixel_value = pixel_value + TWOPI;
            else
                wrapped_pixel_value = pixel_value;
            return wrapped_pixel_value;
        }

        // pixelL_value is the left pixel,  pixelR_value is the right pixel
        private static int find_wrap(float pixelL_value, float pixelR_value)
        {
            float difference;
            int wrap_value;
            difference = pixelL_value - pixelR_value;

            if (difference > PI)
                wrap_value = -1;
            else if (difference < -PI)
                wrap_value = 1;
            else
                wrap_value = 0;

            return wrap_value;
        }

        private static void calculate_reliability(float* wrappedImage, PIXELM* pixel, int image_width,
                                   int image_height, params_t* myparams)
        {
            int image_width_plus_one = image_width + 1;
            int image_width_minus_one = image_width - 1;
            PIXELM* pixel_pointer = pixel + image_width_plus_one;
            float* WIP =
                wrappedImage + image_width_plus_one;  // WIP is the wrapped image pointer
            float H, V, D1, D2;
            int i, j;

            for (i = 1; i < image_height - 1; ++i)
            {
                for (j = 1; j < image_width - 1; ++j)
                {
                    H = wrap(*(WIP - 1) - *WIP) - wrap(*WIP - *(WIP + 1));
                    V = wrap(*(WIP - image_width) - *WIP) -
                        wrap(*WIP - *(WIP + image_width));
                    D1 = wrap(*(WIP - image_width_plus_one) - *WIP) -
                            wrap(*WIP - *(WIP + image_width_plus_one));
                    D2 = wrap(*(WIP - image_width_minus_one) - *WIP) -
                            wrap(*WIP - *(WIP + image_width_minus_one));
                    pixel_pointer->reliability = H * H + V * V + D1 * D1 + D2 * D2;
                    pixel_pointer++;
                    WIP++;
                }
                pixel_pointer += 2;
                WIP += 2;
            }
        }


        // calculate the reliability of the horizontal edges of the image
        // it is calculated by adding the reliability of pixel and the relibility of
        // its right-hand neighbour
        // edge is calculated between a pixel and its next neighbour
        private static void horizontalEDGEs(PIXELM* pixel, EDGE* edge, int image_width,
                             int image_height, params_t* myparams)
        {
            EDGE* edge_pointer = edge;
            PIXELM* pixel_pointer = pixel;
            int no_of_edges = myparams->no_of_edges;

            for (int i = 0; i < image_height; i++)
            {
                for (int j = 0; j < image_width-1; j++)
                {
                    //if (pixel_pointer->input_mask == NOMASK &&
                    //    (pixel_pointer + 1)->input_mask == NOMASK)
                    //{
                    edge_pointer->pointer_1 = pixel_pointer;
                    edge_pointer->pointer_2 = (pixel_pointer + 1);
                    edge_pointer->reliab = pixel_pointer->reliability + (pixel_pointer + 1)->reliability;
                    edge_pointer->increment = find_wrap(pixel_pointer->value, (pixel_pointer + 1)->value);
                    edge_pointer++;
                    no_of_edges++;
                    //}
                    pixel_pointer++;
                }
                //pixel_pointer++;
            }
            myparams->no_of_edges = no_of_edges;
        }

        // calculate the reliability of the vertical edges of the image
        // it is calculated by adding the reliability of pixel and the relibility of
        // its lower neighbour in the image.
        private static void verticalEDGEs(PIXELM* pixel, EDGE* edge, int image_width, int image_height,
                           params_t* myparams)
        {
            int no_of_edges = myparams->no_of_edges;
            PIXELM* pixel_pointer = pixel;
            EDGE* edge_pointer = edge + no_of_edges;

            for (int i = 0; i < image_height - 1; i++)
            {
                for (int j = 0; j < image_width; j++)
                {
                    //if (pixel_pointer->input_mask == NOMASK &&
                    //    (pixel_pointer + image_width)->input_mask == NOMASK)
                    //{
                    edge_pointer->pointer_1 = pixel_pointer;
                    edge_pointer->pointer_2 = (pixel_pointer + image_width);
                    edge_pointer->reliab = pixel_pointer->reliability +
                                            (pixel_pointer + image_width)->reliability;
                    edge_pointer->increment = find_wrap(
                        pixel_pointer->value, (pixel_pointer + image_width)->value);
                    edge_pointer++;
                    no_of_edges++;
                    //}
                    pixel_pointer++;
                }  // j loop
            }  // i loop
            myparams->no_of_edges = no_of_edges;
        }


        // gather the pixels of the image into groups
        private static void gatherPIXELs(EDGE* edge, params_t* myparams)
        {
            PIXELM* PIXEL1;
            PIXELM* PIXEL2;
            PIXELM* group1;
            PIXELM* group2;
            EDGE* pointer_edge = edge;
            int incremento;
            int computed_edges = 0;
            int non_computed_edges = 0;
            for (int k = 0; k < myparams->no_of_edges; k++)
            {
                PIXEL1 = pointer_edge->pointer_1;
                PIXEL2 = pointer_edge->pointer_2;

                // PIXELM 1 and PIXELM 2 belong to different groups
                // initially each pixel is a group by it self and one pixel can construct a
                // group
                // no else or else if to this if
                if (PIXEL2 != null & PIXEL2 != null)
                {
                    computed_edges += 1;
                    if (PIXEL2->head != PIXEL1->head) // SJ added check here for if PIXEL2-> head or pixel2-> head is null
                    {
                        // PIXELM 2 is alone in its group
                        // merge this pixel with PIXELM 1 group and find the number of 2 pi to add
                        // to or subtract to unwrap it
                        if ((PIXEL2->next == null) && (PIXEL2->head == PIXEL2))
                        {
                            PIXEL1->head->last->next = PIXEL2;
                            PIXEL1->head->last = PIXEL2;
                            (PIXEL1->head->number_of_pixels_in_group)++;
                            PIXEL2->head = PIXEL1->head;
                            PIXEL2->increment = PIXEL1->increment - pointer_edge->increment;
                        }

                        // PIXELM 1 is alone in its group
                        // merge this pixel with PIXELM 2 group and find the number of 2 pi to add
                        // to or subtract to unwrap it
                        else if ((PIXEL1->next == null) && (PIXEL1->head == PIXEL1))
                        {
                            PIXEL2->head->last->next = PIXEL1;
                            PIXEL2->head->last = PIXEL1;
                            (PIXEL2->head->number_of_pixels_in_group)++;
                            PIXEL1->head = PIXEL2->head;
                            PIXEL1->increment = PIXEL2->increment + pointer_edge->increment;
                        }

                        // PIXELM 1 and PIXELM 2 both have groups
                        else
                        {
                            group1 = PIXEL1->head;
                            group2 = PIXEL2->head;
                            // if the no. of pixels in PIXELM 1 group is larger than the
                            // no. of pixels in PIXELM 2 group.  Merge PIXELM 2 group to
                            // PIXELM 1 group and find the number of wraps between PIXELM 2
                            // group and PIXELM 1 group to unwrap PIXELM 2 group with respect
                            // to PIXELM 1 group.  the no. of wraps will be added to PIXELM 2
                            // group in the future
                            if (group1->number_of_pixels_in_group >
                                group2->number_of_pixels_in_group)
                            {
                                // merge PIXELM 2 with PIXELM 1 group
                                group1->last->next = group2;
                                group1->last = group2->last;
                                group1->number_of_pixels_in_group =
                                    group1->number_of_pixels_in_group +
                                    group2->number_of_pixels_in_group;
                                incremento =
                                    PIXEL1->increment - pointer_edge->increment - PIXEL2->increment;
                                // merge the other pixels in PIXELM 2 group to PIXELM 1 group
                                while (group2 != null)
                                {
                                    group2->head = group1;
                                    group2->increment += incremento;
                                    group2 = group2->next;
                                }
                            }

                            // if the no. of pixels in PIXELM 2 group is larger than the
                            // no. of pixels in PIXELM 1 group.  Merge PIXELM 1 group to
                            // PIXELM 2 group and find the number of wraps between PIXELM 2
                            // group and PIXELM 1 group to unwrap PIXELM 1 group with respect
                            // to PIXELM 2 group.  the no. of wraps will be added to PIXELM 1
                            // group in the future
                            else
                            {
                                // merge PIXELM 1 with PIXELM 2 group
                                group2->last->next = group1;
                                group2->last = group1->last;
                                group2->number_of_pixels_in_group =
                                    group2->number_of_pixels_in_group +
                                    group1->number_of_pixels_in_group;
                                incremento =
                                    PIXEL2->increment + pointer_edge->increment - PIXEL1->increment;
                                // merge the other pixels in PIXELM 2 group to PIXELM 1 group
                                while (group1 != null)
                                {
                                    group1->head = group2;
                                    group1->increment += incremento;
                                    group1 = group1->next;
                                }  // while

                            }  // else
                        }  // else
                    }  // if
                    pointer_edge++;
                }
                else
                {
                    non_computed_edges += 1;
                    pointer_edge++;
                }
            }
        }

        // unwrap the image
        private static void unwrapImage(PIXELM* pixel, int image_width, int image_height)
        {
            int i;
            int image_size = image_width * image_height;
            PIXELM* pixel_pointer = pixel;

            for (i = 0; i < image_size; i++)
            {
                if (pixel_pointer->increment != 0)
                {
                    pixel_pointer->value += TWOPI * (float)(pixel_pointer->increment);
                }
                pixel_pointer++;
            }
        }

        // the input to this unwrapper is an array that contains the wrapped
        // phase map.  copy the image on the buffer passed to this unwrapper to
        // over-write the unwrapped phase map on the buffer of the wrapped
        // phase map.
        private static void returnImage(PIXELM* pixel, float* unwrapped_image, int image_width,
                         int image_height)
        {
            int i;
            int image_size = image_width * image_height;
            float* unwrapped_image_pointer = unwrapped_image;
            PIXELM* pixel_pointer = pixel;

            for (i = 0; i < image_size; i++)
            {
                *unwrapped_image_pointer = pixel_pointer->value;
                pixel_pointer++;
                unwrapped_image_pointer++;
            }
        }

        // the main function of the unwrapper
        public static float[] Unwrap2D(float[] wrapped_image, float[] unwrapped_image,
                      int image_width, int image_height)
        {
            // SJ, 200419
            // Original algorithm took an input_mask for unwrapping, so that the user could specify pixels they weren't interested in, 
            // an int indicating if the phase unwrapping should loop the x-image around such that the left border of the
            // image is unwrapped against the right border (wrap_around_x) and similar for top and bottom borders (wrap_around_y). 
            // These are int values representing booleans (0:False, 1: True). 
            // I removed the arguments to facilitate a cleaner interface. 

            // params_t does not support initialization single-line - initializing here
            params_t myparams;
            myparams.mod = TWOPI;
            //myparams.x_connectivity = 0; // wrap_around_x
            //myparams.y_connectivity = 0; // wrap_around_y
            myparams.no_of_edges = 0;

            int image_size = image_height * image_width;
            int No_of_Edges_initially = 2 * image_width * image_height + 2 * image_width;

            // Allocate arrays that will be modified by the program
            //byte[] input_mask = new byte[image_size];
            //for (int i = 0; i < image_size; i++)
            //{
            //    input_mask[i] = NOMASK;
            //}
            // Initialize arrays for extended_mask, PIXELM, and EDGES
            //byte[] extended_mask = new byte[image_size];
            PIXELM[] pixel_array = new PIXELM[image_size];
            EDGE[] edge_array = new EDGE[No_of_Edges_initially];

            // Use the fixed statements to maintain arrays?
            fixed (float* wrapped_image_ptr = &wrapped_image[0])
            {
                fixed (float* unwrapped_image_ptr = &unwrapped_image[0])
                {
                    fixed (PIXELM* pixel = &pixel_array[0])
                    {
                        fixed (EDGE* edge = &edge_array[0])
                        {
                            initialisePIXELs(wrapped_image_ptr, pixel, image_width, image_height);
                            calculate_reliability(wrapped_image_ptr, pixel, image_width, image_height,
                                                    &myparams);

                            // TEST/DEBUG: Create a new array from the reliabilities to test
                            //float[] reliab = new float[image_size];
                            //for (int pp=0; pp<image_size; pp++)
                            //{
                            //    reliab[pp] = pixel_array[pp].reliability;
                            //}
                            //File.WriteAllBytes(@"C:\Reliabilities", byte[] bytes)
                            horizontalEDGEs(pixel, edge, image_width, image_height, &myparams);
                            verticalEDGEs(pixel, edge, image_width, image_height, &myparams); // Swapped the order of these (horz, vertical originally - that kept failing)
                        }
                        // Use C# LINQ sorting 
                        if (myparams.no_of_edges != 0)
                        {
                            // sort the EDGEs depending on their reiability. The PIXELs with higher
                            // relibility (small value) first
                            Array.Sort<EDGE>(edge_array, (x, y) => x.reliab.CompareTo(y.reliab));
                        }
                        fixed (EDGE* edge = &edge_array[0])
                        {
                            // gather PIXELs into groups
                            gatherPIXELs(edge, &myparams);
                            unwrapImage(pixel, image_width, image_height);
                            //maskImage(pixel, input_mask_ptr, image_width, image_height);

                            // copy the image from PIXELM structure to the unwrapped phase array
                            // passed to this function
                            returnImage(pixel, unwrapped_image_ptr, image_width, image_height);
                        }
                    }
                }
            }
            // Return the unwrapped_mask
            return unwrapped_image;
        }
    }
}
