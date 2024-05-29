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
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <pthread.h>
#include <signal.h>
#include <sys/wait.h>

#define MAX_PROCESSES 100

typedef enum { RUNNING, READY, BLOCKED }
ProcessState;

typedef struct {
    int pid;
    char type; // 'F': FG, 'B': BG
    int remainingTime; // Remaining time in WQ
    char promoted; // Promotion status '*' or ' '
    ProcessState state;
}
Process;

// Global variables
Process dq[MAX_PROCESSES];
Process wq[MAX_PROCESSES];
int dq_count = 0;
int wq_count = 0;
int current_pid = 0;
pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;
volatile sig_atomic_t running = 1;

// Function prototypes
void signal_handler(int sig);
void add_to_dq(Process p);
void add_to_wq(Process p);
void* shell_process(void* arg);
void print_queues();
void wake_up_processes();
void* monitor_process(void* arg);
void schedule_processes();
char** parse(const char* command);
void exec(char** args);

void signal_handler(int sig)
{
    if (sig == SIGINT)
    {
        printf("\nSIGINT received, shutting down...\n");
        running = 0;
    }
}

void add_to_dq(Process p)
{
    pthread_mutex_lock(&lock) ;
    if (dq_count < MAX_PROCESSES)
    {
        dq[dq_count++] = p;
    }
    else
    {
        printf("DQ Overflow\n");
    }
    pthread_mutex_unlock(&lock) ;
}

void add_to_wq(Process p)
{
    pthread_mutex_lock(&lock) ;
    if (wq_count < MAX_PROCESSES)
    {
        wq[wq_count++] = p;
        // Sort by remaining time
        for (int i = 0; i < wq_count - 1; i++)
        {
            for (int j = 0; j < wq_count - i - 1; j++)
            {
                if (wq[j].remainingTime > wq[j + 1].remainingTime)
                {
                    Process temp = wq[j];
                    wq[j] = wq[j + 1];
                    wq[j + 1] = temp;
                }
            }
        }
    }
    else
    {
        printf("WQ Overflow\n");
    }
    pthread_mutex_unlock(&lock) ;
}

void* shell_process(void* arg)
{
    char input[100];
    while (running)
    {
        printf("Shell: Enter a command: ");
        if (fgets(input, sizeof(input), stdin) == NULL) break;
        input[strcspn(input, "\n")] = '\0'; // Remove newline character

        if (strcmp(input, "exit") == 0)
        {
            running = 0;
            break;
        }

        char** args = parse(input);
        if (args)
        {
            exec(args);
            // Free args memory
            int i = 0;
            while (args[i])
            {
                free(args[i]);
                i++;
            }
            free(args);
        }
    }
    return NULL;
}

void print_queues()
{
    pthread_mutex_lock(&lock) ;
    printf("DQ: ");
    for (int i = 0; i < dq_count; i++)
    {
        printf("%d%c%s(%s) ", dq[i].pid, dq[i].type, dq[i].promoted == '*' ? "*" : "",
               dq[i].state == RUNNING ? "RUNNING" :
               dq[i].state == READY ? "READY" : "BLOCKED");
    }
    printf("\nWQ: ");
    for (int i = 0; i < wq_count; i++)
    {
        printf("%d%c:%d(%s) ", wq[i].pid, wq[i].type, wq[i].remainingTime,
               wq[i].state == READY ? "READY" : "BLOCKED");
    }
    printf("\n");
    pthread_mutex_unlock(&lock) ;
}

void wake_up_processes()
{
    pthread_mutex_lock(&lock) ;
    int i = 0;
    while (i < wq_count)
    {
        wq[i].remainingTime--;
        if (wq[i].remainingTime <= 0)
        {
            Process p = wq[i];
            for (int j = i; j < wq_count - 1; j++)
            {
                wq[j] = wq[j + 1];
            }
            wq_count--;
            p.state = READY;
            add_to_dq(p);
        }
        else
        {
            i++;
        }
    }
    pthread_mutex_unlock(&lock) ;
}

void* monitor_process(void* arg)
{
    while (running)
    {
        wake_up_processes();
        printf("Monitor: Checking queues\n");
        print_queues();
        sleep(3); // Print status every 3 seconds
    }
    return NULL;
}

void schedule_processes()
{
    pthread_mutex_lock(&lock) ;

    // Execute processes in DQ
    while (dq_count > 0)
    {
        Process p = dq[0];
        for (int i = 0; i < dq_count - 1; i++)
        {
            dq[i] = dq[i + 1];
        }
        dq_count--;

        // Execute process
        printf("Executing process %d%c%s\n", p.pid, p.type, p.promoted == '*' ? "*" : "");
        p.state = RUNNING;
        pthread_mutex_unlock(&lock) ; // Unlock while executing process to avoid holding the lock during sleep
        sleep(1); // Execution time (1 second)
        pthread_mutex_lock(&lock) ; // Lock again to update the process state
        p.remainingTime--;

        // Update process state
        if (p.remainingTime > 0)
        {
            // Check process promotion
            if (p.type == 'B' && p.remainingTime <= 3)
            {
                p.promoted = '*';
                printf("Process %d%c%s promoted\n", p.pid, p.type, p.promoted);
            }
            p.state = BLOCKED;
            add_to_wq(p);
        }
        else
        {
            printf("Process %d%c%s finished\n", p.pid, p.type, p.promoted == '*' ? "*" : "");
        }
    }

    pthread_mutex_unlock(&lock) ;
}

char** parse(const char* command)
{
    char** tokens = malloc(100 * sizeof(char*)); // Max 100 tokens
    if (tokens == NULL)
    {
        perror("malloc failed");
        return NULL;
    }
    char* cmd_copy = strdup(command); // Copy original string
    if (cmd_copy == NULL)
    {
        perror("strdup failed");
        free(tokens);
        return NULL;
    }
    char* token;
    int i = 0;

    token = strtok(cmd_copy, " ");
    while (token != NULL)
    {
        tokens[i++] = strdup(token); // Copy and assign token
        if (tokens[i - 1] == NULL)
        {
            perror("strdup failed");
            // Free already allocated memory
            for (int j = 0; j < i - 1; j++)
            {
                free(tokens[j]);
            }
            free(tokens);
            free(cmd_copy);
            return NULL;
        }
        token = strtok(NULL, " ");
    }
    tokens[i] = NULL; // Last token is NULL

    free(cmd_copy); // Free copied original string
    return tokens;
}

void exec(char** args)
{
    pid_t pid = fork();
    if (pid == 0)
    {
        // Execute command in child process
        if (execvp(args[0], args) < 0)
        {
            perror("execvp failed");
            exit(1);
        }
    }
    else if (pid > 0)
    {
        // Wait for child process to finish in parent process
        int status;
        waitpid(pid, &status, 0);
    }
    else
    {
        perror("fork failed");
    }
}

int main()
{
    signal(SIGINT, signal_handler); // Handle SIGINT for graceful shutdown

    pthread_t shell_tid, monitor_tid;

    // Create Shell and Monitor threads
    pthread_create(&shell_tid, NULL, shell_process, NULL);
    pthread_create(&monitor_tid, NULL, monitor_process, NULL);

    // Simulate scheduling process
    while (running)
    {
        schedule_processes();
        sleep(1); // Run scheduler every second
    }

    pthread_join(shell_tid, NULL);
    pthread_join(monitor_tid, NULL);

    pthread_mutex_destroy(&lock) ;

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