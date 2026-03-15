#include<fstream>
#include<iostream>
#include<cmath>
using namespace std;
ifstream fin("date.txt");
int recursive(int n){
    if(n == 0)
      return 0;
    else
      return 1 + recursive(n - 1);
}
int main(){
    int n, sum = 0;
    cin >> n;
    cout << recursive(n);
    return 0;
}