#include <iostream>
#include <cstdlib> // rand() 함수 사용을 위함
#include <ctime>   // 시간 기반 시드 생성을 위함
#include <list>    // 리스트 컨테이너 사용을 위함
#include <mutex>   // 뮤텍스 사용을 위함
#include <thread>  // 스레드 사용을 위함
#include <chrono>  // 시간 기반 대기를 위함

using namespace std;

// 프로세스 구조체
struct Process
{
    int pid;
    bool isForeground; // foreground인 경우 true, background인 경우 false
    int remainingTime; // 백그라운드 프로세스의 남은 시간
    Process(int id, bool fg = true) : pid(id), isForeground(fg), remainingTime(0) { }
};

// 스택 노드 구조체
struct StackNode
{
    list<Process> processList;
    StackNode* next;
    StackNode() : next(nullptr) { }
};

// 동적 큐 클래스
class DynamicQueue
{
    private:
    StackNode* top;    // 스택의 맨 위 (foreground 프로세스)
    StackNode* bottom; // 스택의 맨 아래 (background 프로세스)
    mutex mtx;         // 스레드 안전을 위한 뮤텍스

    public:
    DynamicQueue() : top(nullptr), bottom(nullptr) { }

    // 프로세스 enqueue 메서드
    void enqueue(Process p)
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = p.isForeground ? top : bottom;
        targetNode = ensureNode(targetNode);
        targetNode->processList.push_back(p);
    }

    // 프로세스 dequeue 메서드
    Process dequeue()
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = top;
        if (top && !top->processList.empty())
        {
            Process p = top->processList.front();
            top->processList.pop_front();
            if (top->processList.empty())
            {
                top = top->next;
                delete targetNode;
            }
            return p;
        }
        return Process(-1, true); // 큐가 비어있는 경우 더미 프로세스 반환
    }

    // 프로세스 프로모션 메서드
    void promote()
    {
        lock_guard < mutex > lock (mtx) ;
        if (top && top->next)
        {
            StackNode* promotedNode = top;
            top = top->next;
            promotedNode->next = nullptr;
            if (promotedNode->processList.empty())
            {
                delete promotedNode;
            }
            else
            {
                StackNode* targetNode = top;
                while (targetNode->next)
                {
                    targetNode = targetNode->next;
                }
                targetNode->next = promotedNode;
            }
        }
    }

    // 프로세스 분할 및 병합 메서드
    void split_n_merge(int threshold)
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = top;
        while (targetNode)
        {
            if (targetNode->processList.size() > threshold)
            {
                StackNode* newNode = new StackNode();
                auto it = targetNode->processList.begin();
                advance(it, threshold / 2);
                newNode->processList.splice(newNode->processList.begin(), targetNode->processList, targetNode->processList.begin(), it);
                newNode->next = targetNode->next;
                targetNode->next = newNode;
            }
            targetNode = targetNode->next;
        }
    }

    // 프로세스 유형(foreground/background)에 대한 노드의 존재 여부 확인 메서드
    StackNode* ensureNode(StackNode* node)
    {
        if (!node)
        {
            node = new StackNode();
            if (node->processList.empty())
            {
                if (node->next == nullptr)
                {
                    if (node->processList.empty())
                        top = bottom = node;
                }
                else if (node->processList.empty())
                    top = node;
            }
            else
            {
                if (node->next == nullptr)
                {
                    if (node->processList.empty())
                        bottom = node;
                }
            }
        }
        return node;
    }

    // 큐 내용 출력 메서드
    void printQueue()
    {
        lock_guard < mutex > lock (mtx) ;
        cout << "Foreground Processes:" << endl;
        StackNode* current = top;
        while (current)
        {
            for (const auto&process : current->processList) {
                cout << process.pid << (process.isForeground ? " (F)" : " (B)");
                if (!process.isForeground)
                    cout << " - 남은 시간: " << process.remainingTime;
                cout << endl;
            }
            current = current->next;
        }
    }
};

// 프로세스 시뮬레이션 함수
void simulateProcesses(DynamicQueue& queue)
{
    srand(time(nullptr));
    int count = 0;
    while (count < 20)
    { // 20번의 반복으로
        // 프로세스 생성 시뮬레이션
        int pid = rand() % 100; // 랜덤 프로세스 ID 생성
        bool isForeground = rand() % 2 == 0; // 프로세스가 foreground 또는 background인지 랜덤으로 결정
        Process p(pid, isForeground);
        if (!isForeground)
            p.remainingTime = rand() % 10 + 1; // 백그라운드 프로세스의 남은 시간 랜덤 생성

        // 프로세스 enqueue
        queue.enqueue(p);

        // 프로세스 프로모션 및 분할/병합
        queue.promote();
        queue.split_n_merge(5); // 데모를 위해 임계값을 5로 설정

        // 큐 출력
        queue.printQueue();

        // 실제 시간 처리를 시뮬레이션하기 위해 짧은 시간 동안 sleep
        this_thread::sleep_for(chrono::seconds(1));

        count++;
    }
}

int main()
{
    DynamicQueue queue;
    simulateProcesses(queue);
    return 0;
}//2-1
#include <iostream>
#include <cstdlib> // for exit()
#include <cstring> // for strtok(), strtok_r()
#include <unistd.h> // for fork(), execvp(), wait()
#include <thread>
#include <chrono>
#include <list>
#include <mutex>
#include <algorithm>
#include <sys/wait.h> // for wait function

using namespace std;

// 프로세스 구조체
struct Process
{
    int pid; // 프로세스 ID
    bool isForeground; // 포그라운드인 경우 true, 백그라운드인 경우 false
    int remainingTime; // 백그라운드 프로세스의 남은 시간
    Process(int id, bool fg = true) : pid(id), isForeground(fg), remainingTime(0) { }
};

// 스택 노드 구조체
struct StackNode
{
    list<Process> processList; // 프로세스 리스트
    StackNode* next; // 다음 노드를 가리키는 포인터
    StackNode() : next(nullptr) { }
};

class DynamicQueue
{
    private:
    StackNode* top; // 스택의 맨 위 (포그라운드 프로세스)
    StackNode* bottom; // 스택의 맨 아래 (백그라운드 프로세스)
    mutex mtx; // 스레드 안전을 보장하기 위한 뮤텍스

    public:
    DynamicQueue() : top(nullptr), bottom(nullptr) { }

    // 프로세스를 큐에 추가
    void enqueue(Process p)
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = p.isForeground ? top : bottom;
        targetNode = ensureNode(targetNode);
        targetNode->processList.push_back(p);
    }

    // 프로세스를 큐에서 제거
    Process dequeue()
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = top;
        if (top && !top->processList.empty())
        {
            Process p = top->processList.front();
            top->processList.pop_front();
            if (top->processList.empty())
            {
                top = top->next;
                delete targetNode;
            }
            return p;
        }
        return Process(-1, true); // 큐가 비어 있는 경우 더미 프로세스 반환
    }

    // 프로세스를 프로모션
    void promote()
    {
        lock_guard < mutex > lock (mtx) ;
        if (top && top->next)
        {
            StackNode* promotedNode = top;
            top = top->next;
            promotedNode->next = nullptr;
            if (promotedNode->processList.empty())
            {
                delete promotedNode;
            }
            else
            {
                StackNode* targetNode = top;
                while (targetNode->next)
                {
                    targetNode = targetNode->next;
                }
                targetNode->next = promotedNode;
            }
        }
    }

    // 프로세스를 분할하거나 병합
    void split_n_merge(int threshold)
    {
        lock_guard < mutex > lock (mtx) ;
        StackNode* targetNode = top;
        while (targetNode)
        {
            if (targetNode->processList.size() > threshold)
            {
                StackNode* newNode = new StackNode();
                auto it = targetNode->processList.begin();
                advance(it, threshold / 2);
                newNode->processList.splice(newNode->processList.begin(), targetNode->processList, targetNode->processList.begin(), it);
                newNode->next = targetNode->next;
                targetNode->next = newNode;
            }
            targetNode = targetNode->next;
        }
    }

    // 주어진 프로세스 유형 (포그라운드/백그라운드)에 대한 노드의 존재 여부 확인
    StackNode* ensureNode(StackNode* node)
    {
        if (!node)
        {
            node = new StackNode();
            if (node->processList.empty())
            {
                if (node->next == nullptr)
                {
                    if (node->processList.empty())
                        top = bottom = node;
                }
                else if (node->processList.empty())
                    top = node;
            }
            else
            {
                if (node->next == nullptr)
                {
                    if (node->processList.empty())
                        bottom = node;
                }
            }
        }
        return node;
    }

    // 큐의 내용을 출력
    void printQueue()
    {
        lock_guard < mutex > lock (mtx) ;
        cout << "DQ: ";
        StackNode* current = bottom;
        while (current)
        {
            for (const auto&process : current->processList) {
                cout << "[" << process.pid << (process.isForeground ? "F" : "B") << "]";
            }
            if (current == top)
            {
                cout << " (top)";
            }
            if (current == bottom)
            {
                cout << " (bottom)";
            }
            cout << "-";
            current = current->next;
        }
        cout << endl;
    }
};

// 프로세스 시뮬레이션 함수
void simulateProcesses(DynamicQueue& queue)
{
    srand(time(nullptr));
    int count = 0;
    while (count < 20)
    { // 20번 반복하여 시뮬레이션 실행
        // 프로세스 생성 시뮬레이션
        int pid = rand() % 100; // 임의의 프로세스 ID 생성
        bool isForeground = rand() % 2 == 0; // 프로세스가 포그라운드인지 백그라운드인지 무작위로 결정
        Process p(pid, isForeground);
        if (!isForeground)
            p.remainingTime = rand() % 10 + 1; // 백그라운드 프로세스의 임의의 남은 시간 설정

        // 프로세스 큐에 추가
        queue.enqueue(p);

        // 프로세스 프로모션 및 분할/병합
        queue.promote();
        queue.split_n_merge(5); // 5를 임의의 임계값으로 설정 (시연용)

        // 큐 출력
        queue.printQueue();

        // 실제 시간 경과 시뮬레이션
        this_thread::sleep_for(chrono::seconds(1));

        count++;
    }
}
// Parse 함수: 명령을 입력 받아 토큰으로 파싱하여 반환
char** parse(const char* command)
{
    char** args = new char*[64]; // 최대 64개의 토큰을 저장할 수 있는 배열
    char* temp = strdup(command); // 입력 명령을 복제하여 임시로 저장

    int i = 0;
    char* token = strtok(temp, " "); // 공백을 기준으로 첫 번째 토큰 추출
    while (token != nullptr)
    {
        args[i++] = token; // 추출한 토큰을 배열에 저장
        token = strtok(nullptr, " "); // 다음 토큰 추출
    }
    args[i] = nullptr; // 배열의 끝을 나타내는 nullptr 추가

    delete[] temp; // 복제한 문자열 메모리 해제
    return args;
}

// Exec 함수: 명령어를 실행
void exec(char** args)
{
    pid_t pid = fork(); // 새로운 프로세스 생성
    if (pid == -1)
    {
        perror("fork failed");
        exit(EXIT_FAILURE);
    }
    else if (pid == 0)
    {
        // 자식 프로세스일 경우
        if (execvp(args[0], args) == -1)
        {
            perror("execvp failed");
            exit(EXIT_FAILURE);
        }
    }
    else
    {
        // 부모 프로세스일 경우
        wait(nullptr); // 자식 프로세스의 종료를 기다림
    }
}

int main()
{
    DynamicQueue queue;
    simulateProcesses(queue);
    return 0;
}//2-2
#include <iostream>
#include <fstream>
#include <vector>
#include <thread>
#include <chrono>
#include <mutex>
#include <sstream>
#include <algorithm>

std::mutex mu;

void executeCommand(const std::string& command, int period, int duration, int n, int m);

int gcd(int a, int b);
int countPrimes(int x);
long long sumUpTo(int x, int m);
void dummy();

int main()
{
    std::ifstream file("commands.txt");
    if (!file)
    {
        std::cerr << "파일을 열 수 없습니다." << std::endl;
        return 1; // 파일 오픈 실패
    }

    std::string line;
    std::vector<std::thread> threads;

    while (getline(file, line))
    {
        std::istringstream iss(line);
        std::string cmd;
        int period = 0, duration = 100, n = 1, m = 1;

        std::string token;
        while (iss >> token)
        {
            if (token == "-p" && iss >> token) period = std::stoi(token);
            else if (token == "-d" && iss >> token) duration = std::stoi(token);
            else if (token == "-n" && iss >> token) n = std::stoi(token);
            else if (token == "-m" && iss >> token) m = std::stoi(token);
            else cmd += token + " ";
        }

        if (!cmd.empty()) cmd.pop_back(); // Remove trailing space
        threads.emplace_back(executeCommand, cmd, period, duration, n, m);
    }

    for (auto & t : threads) {
        if (t.joinable()) t.join();
    }

    return 0;
}

void executeCommand(const std::string& command, int period, int duration, int n, int m)
{
    auto start = std::chrono::steady_clock::now();
    do
    {
        for (int i = 0; i < n; ++i)
        {
            {
                std::unique_lock < std::mutex > lock (mu) ;
                if (command.substr(0, 5) == "echo ")
                {
                    std::cout << command.substr(5) << std::endl; // Print the string after "echo "
                }
                else if (command.substr(0, 4) == "gcd ")
                {
                    std::stringstream ss(command.substr(4));
        int x, y;
        ss >> x >> y;
        std::cout << gcd(x, y) << std::endl;
    }
                else if (command.substr(0, 6) == "prime ")
    {
        int x = std::stoi(command.substr(6));
        std::cout << countPrimes(x) << std::endl;
    }
    else if (command.substr(0, 4) == "sum ")
    {
        int x = std::stoi(command.substr(4));
        std::cout << sumUpTo(x, m) << std::endl;
    }
    else if (command == "dummy")
    {
        dummy();
    }
}
if (period > 0)
{
    std::this_thread::sleep_for(std::chrono::seconds(period));
}
        }
    } while (std::chrono::duration_cast<std::chrono::seconds>(std::chrono::steady_clock::now() - start).count() < duration) ;
}

int gcd(int a, int b)
{
    return b == 0 ? a : gcd(b, a % b);
}

int countPrimes(int x)
{
    std::vector<bool> prime(x +1, true);
    prime[0] = prime[1] = false;
    for (int p = 2; p * p <= x; ++p)
    {
        if (prime[p])
        {
            for (int i = p * p; i <= x; i += p)
            {
                prime[i] = false;
            }
        }
    }
    return std::count(prime.begin(), prime.end(), true);
}

long long sumUpTo(int x, int m) {
    long long sum = 0;
for (int i = 1; i <= x; ++i)
{
    sum += i;
}
return sum * m;
}

void dummy()
{
    // 아무 일도 하지 않음
}//2-3