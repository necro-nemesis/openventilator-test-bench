//
// Very simple software to control R/C ESC from serial port
//
// Henry Palonen / 25.3.2020

#include <Servo.h>

Servo ESC;     // create servo object to control the ESC

int outValue;  // value from the analog pin

void setup() {
  // Attach the ESC on pin 9
  ESC.attach(9,1000,2000); // (pin, min pulse width, max pulse width in microseconds) 
  Serial.begin(9600);
}

void loop() {

  // check if data has been sent from the computer:
  if (Serial.available()) {
    
    // read the most recent byte (which will be from 0 to 255):
    outValue = Serial.parseInt();
    //Serial.println(outValue);

     if (outValue > -1 && outValue < 101)
     {
        outValue = map(outValue, 0, 100, 0, 180);
        ESC.write(outValue);    // Send the signal to the ESC
     }
  }
}
