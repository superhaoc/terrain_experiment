#pragma kernel ParallelBitonic_B16
#pragma kernel ParallelBitonic_B8
#pragma kernel ParallelBitonic_B4
#pragma kernel ParallelBitonic_B2
#pragma kernel ParallelBitonic_C4
#pragma kernel ParallelBitonic_C2
#pragma kernel ParallelBitonic_B32
#pragma kernel ClearBuffer


#define THREADNUM_X 64
//構造体と比較を自前で定義する必要あり。
//構造体が128byteを超すと共有メモリが使えなくなる

#define data_t uint
#define COMPARISON(a,b) ( a > b )
//ここまで


#define ORDER(a,b) { bool swap = reverse ^ COMPARISON(a,b); \
  data_t auxa = a; data_t auxb = b; if (swap) { a = auxb; b = auxa; } }

#define ORDERV(x,a,b) { bool swap = reverse ^ COMPARISON(x[a],x[b]); \
      data_t auxa = x[a]; data_t auxb = x[b]; \
      if (swap) { x[a] = auxb; x[b] = auxa; } }
#define B2V(x,a) { ORDERV(x,a,a+1) }
#define B4V(x,a) { for (int i4=0;i4<2;i4++) { ORDERV(x,a+i4,a+i4+2) } B2V(x,a) B2V(x,a+2) }
#define B8V(x,a) { for (int i8=0;i8<4;i8++) { ORDERV(x,a+i8,a+i8+4) } B4V(x,a) B4V(x,a+4) }
#define B16V(x,a) { for (int i16=0;i16<8;i16++) { ORDERV(x,a+i16,a+i16+8) } B8V(x,a) B8V(x,a+8) }
#define B32V(x,a) { for (int i32=0;i32<16;i32++) { ORDERV(x,a+i32,a+i32+16) } B16V(x,a) B16V(x,a+16) }


RWStructuredBuffer<data_t> _Data;
groupshared data_t aux[512];
int inc;
int inc0;
int dir;
uint _ClearCount;

[numthreads(THREADNUM_X, 1, 1)]
//void ParallelBitonic_B16(__global data_t* data, int inc, int dir)
void ParallelBitonic_B16(uint threadid : SV_DispatchThreadID)
{
	int t = threadid; // thread index
	int low = t & (inc - 1); // low order bits (below INC)
	int i = ((t - low) << 4) + low; // insert 0000 at position INC
	bool reverse = ((dir & i) == 0); // asc/desc order

	// Load
	data_t x[16];
	for (int k = 0; k < 16; k++) x[k] = _Data[k * inc + i];

	// Sort
	B16V(x, 0)

	// Store
	for (int k = 0; k < 16; k++) _Data[k * inc + i] = x[k];
}

[numthreads(THREADNUM_X, 1, 1)]
void ParallelBitonic_B32(uint threadid : SV_DispatchThreadID)
{
	int t = threadid;
	int low = t & (inc - 1);
	int i = ((t - low) << 5) + low; // insert 00000 at position INC
	bool reverse = ((dir & i) == 0);

	data_t x[32];
	for (int k = 0; k < 32; k++) x[k] = _Data[k * inc + i];

	B32V(x, 0)

	for (int k = 0; k < 32; k++) _Data[k * inc + i] = x[k];
}

[numthreads(THREADNUM_X, 1, 1)]
//void ParallelBitonic_B8(__global data_t* data, int inc, int dir)
void ParallelBitonic_B8(uint threadid : SV_DispatchThreadID)
{
	int t = threadid; // thread index
	int low = t & (inc - 1); // low order bits (below INC)
	int i = ((t - low) << 3) + low; // insert 000 at position INC
	bool reverse = ((dir & i) == 0); // asc/desc order

	// Load
	data_t x[8];
	for (int k = 0; k < 8; k++) x[k] = _Data[k * inc + i];

	// Sort
	B8V(x, 0)

	// Store
	for (int k = 0; k < 8; k++) _Data[k * inc + i] = x[k];
}

[numthreads(THREADNUM_X, 1, 1)]
//void ParallelBitonic_B4(__global data_t* data, int inc, int dir)
void ParallelBitonic_B4(uint threadid : SV_DispatchThreadID)
{
	int t = threadid; // thread index
	int low = t & (inc - 1); // low order bits (below INC)
	int i = ((t - low) << 2) + low; // insert 00 at position INC
	bool reverse = ((dir & i) == 0); // asc/desc order

	// Load
	data_t x0 = _Data[i];
	data_t x1 = _Data[inc + i];
	data_t x2 = _Data[2 * inc + i];
	data_t x3 = _Data[3 * inc + i];

	// Sort
	ORDER(x0, x2)
	ORDER(x1, x3)
	ORDER(x0, x1)
	ORDER(x2, x3)

	// Store
	_Data[i] = x0;
	_Data[inc + i] = x1;
	_Data[2 * inc + i] = x2;
	_Data[3 * inc + i] = x3;
}

[numthreads(THREADNUM_X, 1, 1)]
//void ParallelBitonic_B2(__global data_t* data, int inc, int dir)
void ParallelBitonic_B2(uint threadid : SV_DispatchThreadID)
{
	int t = threadid; // thread index
	int low = t & (inc - 1); // low order bits (below INC)
	int i = (t << 1) - low; // insert 0 at position INC
	bool reverse = ((dir & i) == 0); // asc/desc order

	// Load
	data_t x0 = _Data[i];
	data_t x1 = _Data[inc + i];

	// Sort
	ORDER(x0, x1)

	// Store
	_Data[i] = x0;
	_Data[inc + i] = x1;
}



#define LOCALSIZE_C2 256
[numthreads(LOCALSIZE_C2, 1, 1)]
//void ParallelBitonic_C2(__global data_t* data, int inc0, int dir)
void ParallelBitonic_C2(uint threadid : SV_DispatchThreadID)
{
	int t = threadid; // thread index
	int wgBits = 2 * LOCALSIZE_C2 - 1; // bit mask to get index in local memory AUX (size is 2*WG)

	for (int inc = inc0; inc > 0; inc >>= 1)
	{
		int low = t & (inc - 1); // low order bits (below INC)
		int i = (t << 1) - low; // insert 0 at position INC
		bool reverse = ((dir & i) == 0); // asc/desc order
		data_t x0, x1;

		// Load
		if (inc == inc0)
		{
			// First iteration: load from global memory
			x0 = _Data[i];
			x1 = _Data[i + inc];
		}
		else
		{
			// Other iterations: load from local memory
			GroupMemoryBarrierWithGroupSync();
			x0 = aux[i & wgBits];
			x1 = aux[(i + inc) & wgBits];
		}

		// Sort
		ORDER(x0, x1)

			// Store
			if (inc == 1)
			{
				// Last iteration: store to global memory
				_Data[i] = x0;
				_Data[i + inc] = x1;
			}
			else
			{
				// Other iterations: store to local memory
				GroupMemoryBarrierWithGroupSync();
				aux[i & wgBits] = x0;
				aux[(i + inc) & wgBits] = x1;
			}
	}
}


#define LOCALSIZE_C4 128
[numthreads(LOCALSIZE_C4, 1, 1)]
//void ParallelBitonic_C4(__global data_t* data, int inc0, int dir)
void ParallelBitonic_C4(uint threadid : SV_DispatchThreadID)
{
	int t = threadid; // thread index
	int wgBits = 4 * LOCALSIZE_C4 - 1; // bit mask to get index in local memory AUX (size is 4*WG)
	int inc, low, i;
	bool reverse;
	data_t x[4];

	// First iteration, global input, local output
	inc = inc0 >> 1;
	low = t & (inc - 1); // low order bits (below INC)
	i = ((t - low) << 2) + low; // insert 00 at position INC
	reverse = ((dir & i) == 0); // asc/desc order
	for (int k = 0; k < 4; k++) x[k] = _Data[i + k * inc];
	B4V(x, 0);
	for (int k = 0; k < 4; k++) aux[(i + k * inc) & wgBits] = x[k];
	GroupMemoryBarrierWithGroupSync();

	// Internal iterations, local input and output
	for (; inc > 1; inc >>= 2)
	{
		low = t & (inc - 1); // low order bits (below INC)
		i = ((t - low) << 2) + low; // insert 00 at position INC
		reverse = ((dir & i) == 0); // asc/desc order
		for (int k = 0; k < 4; k++) x[k] = aux[(i + k * inc) & wgBits];
		B4V(x, 0);
		GroupMemoryBarrierWithGroupSync();
		for (int k = 0; k < 4; k++) aux[(i + k * inc) & wgBits] = x[k];
		GroupMemoryBarrierWithGroupSync();
	}

	// Final iteration, local input, global output, INC=1
	i = t << 2;
	reverse = ((dir & i) == 0); // asc/desc order
	for (int k = 0; k < 4; k++) x[k] = aux[(i + k) & wgBits];
	B4V(x, 0);
	for (int k = 0; k < 4; k++) _Data[i + k] = x[k];
}


[numthreads(512, 1, 1)]
void ClearBuffer(uint3 id : SV_DispatchThreadID)
{
	if (id.x < _ClearCount) _Data[id.x] = 0;
}