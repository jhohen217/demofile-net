import sys
import os
import glob
import gzip
import shutil
import subprocess
from datetime import datetime
from pathlib import Path
from typing import List, Union, Tuple, Set

class CollectionsParser:
    PARSER_PATH = "bin/Debug/net7.0/DemoFile.Example.FastParser.exe"

    def __init__(self):
        self.processed = 0
        self.successful = 0
        self.failed = 0
        self.start_time = datetime.now()
        # Track which .dem files were extracted from .dem.gz
        self.extracted_demos: Set[str] = set()
        # Track which .dem files can be safely deleted
        self.processed_demos: Set[str] = set()

    def extract_demo(self, gz_path: str) -> str:
        """Extract a .dem.gz file to the same directory."""
        dem_path = gz_path[:-3]  # Remove .gz extension
        print(f"Extracting {gz_path} to {dem_path}")
        
        try:
            with gzip.open(gz_path, 'rb') as gz_file:
                with open(dem_path, 'wb') as dem_file:
                    shutil.copyfileobj(gz_file, dem_file)
            self.extracted_demos.add(dem_path)
            return dem_path
        except Exception as e:
            print(f"Error extracting {gz_path}: {e}")
            return ""

    def cleanup_demo(self, dem_path: str):
        """Clean up extracted .dem file if it was from a .gz archive."""
        if dem_path in self.extracted_demos and dem_path in self.processed_demos:
            try:
                os.remove(dem_path)
                print(f"Cleaned up extracted demo: {dem_path}")
                self.extracted_demos.remove(dem_path)
                self.processed_demos.remove(dem_path)
            except Exception as e:
                print(f"Error cleaning up {dem_path}: {e}")

    def process_demo(self, file_path: str) -> bool:
        """Process a .dem file through the FastParser."""
        print(f"Processing demo file: {file_path}")
        try:
            result = subprocess.run([
                self.PARSER_PATH,
                file_path  # Pass file path directly as per Program.cs requirements
            ], capture_output=True, text=True)
            
            if result.returncode != 0:
                print(f"Failed to process {file_path}")
                if result.stderr:
                    print(f"Error: {result.stderr}")
                return False
                
            # Mark demo as processed so it can be cleaned up if it was extracted
            self.processed_demos.add(file_path)
            return True
        except Exception as e:
            print(f"Error processing {file_path}: {e}")
            return False

    def get_csv_type(self, file_path: str) -> str:
        """Determine CSV file type based on header.
        Returns:
            'pov' for POV (tick by tick) files
            'kill' for kill collection files
            'unknown' for unrecognized files
        """
        try:
            with open(file_path, 'r', encoding='utf-8-sig') as f:  # utf-8-sig handles BOM
                first_line = f.readline().strip()
                print(f"DEBUG - First line of {file_path}: '{first_line}'")
                # Read next few lines for context
                next_lines = [f.readline().strip() for _ in range(5)]
                print("DEBUG - Next few lines:")
                for line in next_lines:
                    print(f"  '{line}'")
                
                # Clean up header line and compare
                clean_header = first_line.replace('ï»¿', '').strip()
                if clean_header == '[DEMO_INFO]':
                    return 'kill'  # Kill collection files start with [DEMO_INFO]
                elif clean_header == '[KILL_COLLECTION]':
                    return 'pov'  # POV files start with [KILL_COLLECTION]
                return 'unknown'
        except Exception as e:
            print(f"DEBUG - Error reading file: {e}")
            return 'unknown'

    def process_csv(self, file_path: str) -> bool:
        """Process a .csv file through the FastParser."""
        print(f"Processing CSV file: {file_path}")
        
        # Determine CSV type
        csv_type = self.get_csv_type(file_path)
        if csv_type == 'pov':
            print("Processing POV CSV (tick by tick) - extracted demo files will be cleaned up after completion")
        elif csv_type == 'kill':
            print("Processing kill collection CSV - demo files will be preserved for POV processing")
        else:
            print("Unknown CSV type - demo files will be preserved")
            
        try:
            result = subprocess.run([
                self.PARSER_PATH,
                file_path
            ], capture_output=True, text=True)
            
            if result.returncode != 0:
                print(f"Failed to process {file_path}")
                if result.stderr:
                    print(f"Error: {result.stderr}")
                if result.stdout:
                    print(f"Output: {result.stdout}")
                return False
            
            if result.returncode != 0:
                print(f"Failed to process {file_path}")
                if result.stderr:
                    print(f"Error: {result.stderr}")
                return False
                
            # Only clean up demos after successful POV CSV processing
            if csv_type == 'pov':
                print("POV CSV processing complete, cleaning up extracted demo files...")
                for dem_path in list(self.extracted_demos):
                    self.cleanup_demo(dem_path)
                    
            return True
        except Exception as e:
            print(f"Error processing {file_path}: {e}")
            return False

    def process_file(self, file_path: str) -> bool:
        """Process a single file based on its extension."""
        self.processed += 1
        file_start = datetime.now()

        success = False
        path_obj = Path(file_path)
        ext = path_obj.suffix.lower()
        
        if ext == '.gz' and path_obj.stem.lower().endswith('.dem'):
            # Handle .dem.gz files
            dem_path = self.extract_demo(file_path)
            if dem_path:
                success = self.process_demo(dem_path)
                if not success:
                    # Clean up extracted file on failure
                    self.cleanup_demo(dem_path)
        elif ext == '.dem':
            success = self.process_demo(file_path)
        elif ext == '.csv':
            success = self.process_csv(file_path)
            # Cleanup is now handled within process_csv() only for POV CSV processing
        else:
            print(f"Unsupported file type: {file_path}")
            self.failed += 1
            return False

        if success:
            self.successful += 1
        else:
            self.failed += 1

        elapsed = (datetime.now() - file_start).total_seconds()
        print(f"Processed {self.processed} files (Success: {self.successful}, Failed: {self.failed}) - Last file took {elapsed:.1f} seconds\n")
        return success

    def process_directory(self, dir_path: str):
        """Process all .dem, .dem.gz and .csv files in a directory recursively."""
        print(f"\nProcessing folder: {dir_path}")
        print("Listing directory contents:")
        
        # List all matching files first
        dem_files = glob.glob(os.path.join(dir_path, "**/*.dem"), recursive=True)
        demgz_files = glob.glob(os.path.join(dir_path, "**/*.dem.gz"), recursive=True)
        csv_files = glob.glob(os.path.join(dir_path, "**/*.csv"), recursive=True)
        
        all_files = dem_files + demgz_files + csv_files
        for file in all_files:
            print(file)
        print()

        # Process all files
        for file in all_files:
            self.process_file(file)

    def process_input(self, path: str):
        """Process either a file or directory based on input."""
        if os.path.isdir(path):
            self.process_directory(path)
        elif os.path.isfile(path):
            self.process_file(path)
        else:
            print(f"Invalid path: {path}")

    def print_summary(self):
        """Print final processing summary."""
        elapsed = datetime.now() - self.start_time
        minutes = elapsed.seconds // 60
        seconds = elapsed.seconds % 60
        
        print("\nProcessing complete.")
        print(f"Total files processed: {self.processed}")
        print(f"Successful: {self.successful}")
        print(f"Failed: {self.failed}")
        print(f"Total time: {minutes} minutes {seconds} seconds")
        
        # Report any remaining extracted files that weren't cleaned up
        if self.extracted_demos:
            print("\nWarning: Some extracted demo files were not cleaned up:")
            for demo in self.extracted_demos:
                print(f"- {demo}")

def main():
    # Change to script directory
    os.chdir(os.path.dirname(os.path.abspath(__file__)))

    if len(sys.argv) < 2:
        print("Please drag files or folders onto this script")
        input("Press Enter to exit...")
        return

    parser = CollectionsParser()
    
    try:
        # Process each input path
        for path in sys.argv[1:]:
            parser.process_input(path)
    except Exception as e:
        print(f"Error: {e}")
    finally:
        parser.print_summary()
        input("\nPress Enter to exit...")

if __name__ == "__main__":
    main()
